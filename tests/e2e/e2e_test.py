"""End-to-end test: drives the whole system through the API gateway, like the Angular app does.

Needs the Docker Compose stack running (docker compose -f deploy/docker-compose.yml up -d).
Uses only Python's standard library. Run from the repository root:

    python tests/e2e/e2e_test.py

Settings (environment variables): API_URL (default http://localhost:5080), MAILPIT_URL (default
http://localhost:8025, where the emails are read from), SA_PASSWORD (read from deploy/.env if not set).
Every run registers new users, so it can run again without resetting anything.
"""
import json
import os
import subprocess
import sys
import time
import re
import urllib.error
import urllib.parse
import urllib.request

API = os.environ.get("API_URL", "http://localhost:5080")
MAILPIT = os.environ.get("MAILPIT_URL", "http://localhost:8025")
COMPOSE = ["docker", "compose", "-f", "deploy/docker-compose.yml"]
RUN = str(int(time.time()))          # makes this run's emails unique
failures = []

def sa_password():
    if "SA_PASSWORD" in os.environ:
        return os.environ["SA_PASSWORD"]
    with open("deploy/.env") as env:
        for line in env:
            if line.startswith("SA_PASSWORD="):
                return line.split("=", 1)[1].strip()
    sys.exit("SA_PASSWORD is not set and deploy/.env has none")


def call(method, path, body=None, token=None):
    """One HTTP request through the gateway. Returns (status, parsed JSON or None)."""
    req = urllib.request.Request(API + path, method=method,
                                 data=None if body is None else json.dumps(body).encode())
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            status, raw = resp.status, resp.read()
    except urllib.error.HTTPError as err:
        status, raw = err.code, err.read()
    return status, (json.loads(raw) if raw.strip() else None)


def check(name, condition, detail=""):
    print(("PASS  " if condition else "FAIL  ") + name + ("" if condition else f"  ({detail})"))
    if not condition:
        failures.append(name)


def expect(name, method, path, status, body=None, token=None):
    """Calls the API and checks the status code. Returns the response body."""
    actual, data = call(method, path, body, token)
    check(f"{name}: {method} {path} -> {status}", actual == status, f"got {actual}: {data}")
    return data


def sql(query):
    """Runs SQL inside the sqlserver container (used once, to promote the admin)."""
    subprocess.run(COMPOSE + ["exec", "-T", "sqlserver", "/opt/mssql-tools18/bin/sqlcmd", "-C", "-S", "localhost",
                              "-U", "sa", "-P", sa_password(), "-b", "-Q", query], check=True, capture_output=True)


# One route per service, called without a token. Once a service is running it answers itself (401 or 404);
# while it is still starting, the gateway answers 502/503 because there is nothing to forward to.
SERVICE_PROBES = {
    "Identity": "/api/auth/me",
    "Catalog": "/api/policies",
    "Quotes": "/api/quotes/1",
    "Enrollments": "/api/enrollments/mine",
    "Billing": "/api/payments/transactions",
    "Claims": "/api/claims/mine",
    "Notifications": "/api/notifications/preferences",
}

def wait_until_ready(timeout_seconds=300):
    """Waits for the gateway, every service behind it, and Mailpit. Each service only starts listening after
    applying its database scripts, so on a fresh database (as in CI) some take much longer than others."""
    deadline = time.time() + timeout_seconds
    waiting = dict(SERVICE_PROBES, Mailpit=None)
    while waiting and time.time() < deadline:
        for name, path in list(waiting.items()):
            try:
                if path is None:
                    with urllib.request.urlopen(f"{MAILPIT}/api/v1/messages", timeout=5):
                        pass
                elif call("GET", path)[0] in (502, 503, 504):
                    continue
                del waiting[name]
            except OSError:
                pass   # gateway or Mailpit not listening yet
        if waiting:
            time.sleep(2)
    if waiting:
        sys.exit(f"Still not up after {timeout_seconds}s: {', '.join(waiting)}")
    print("All services are up.")


def emails_to(address, subject_contains, timeout=30):
    """The emails Mailpit received for an address whose subject contains the text (waits for the first one)."""
    query = urllib.parse.quote(f'to:"{address}"')
    deadline = time.time() + timeout
    while True:
        with urllib.request.urlopen(f"{MAILPIT}/api/v1/search?query={query}") as response:
            found = [m for m in json.loads(response.read())["messages"] if subject_contains in m["Subject"]]
        if found or time.time() > deadline:
            return found
        time.sleep(0.5)


def verify_email(address):
    """Does what the customer does: opens the verification email and follows its link."""
    message = emails_to(address, "Verify your email")[0]
    with urllib.request.urlopen(f"{MAILPIT}/api/v1/message/{message['ID']}") as response:
        text = json.loads(response.read())["Text"]
    token = re.search(r"verify-email\?token=([A-Za-z0-9_\-]+)", text).group(1)
    expect("verify the email address", "POST", "/api/auth/verify-email", 200, {"token": token})


def register(first, email):
    return {"firstName": first, "lastName": "Tester", "email": email, "password": "Str0ng#Passw0rd",
            "phone": "2035550100", "street": "1 Main St", "city": "Stamford", "state": "CT", "postalCode": "06902"}


def login(email):
    return expect("sign in", "POST", "/api/auth/login", 200, {"email": email, "password": "Str0ng#Passw0rd"})["token"]


def answers_for(policy_id, token):
    """Answers every rating question with its first allowed value."""
    answers = {}
    for q in expect("questions", "GET", f"/api/quotes/questions/{policy_id}", 200, token=token):
        kind = q["kind"]
        answers[q["key"]] = (q.get("min") or 0 if kind == "Number" else q["options"][0] if kind == "Choice"
                             else "1990-01-01" if kind == "Date" else False)
    return answers


def main():
    wait_until_ready()
    today = time.strftime("%Y-%m-%d", time.gmtime())

    # --- Identity: accounts and the admin
    customer_email, admin_email = f"cal.{RUN}@e2e.example", f"ada.{RUN}@e2e.example"
    expect("register customer", "POST", "/api/auth/register", 201, register("Cal", customer_email))
    expect("duplicate email", "POST", "/api/auth/register", 409, register("Cal", customer_email))
    expect("register admin", "POST", "/api/auth/register", 201, register("Ada", admin_email))
    sql(f"UPDATE InsuranceIdentity.accounts.Users SET Role = 'Admin' WHERE Email = '{admin_email}'")
    check("a verification email was sent", len(emails_to(customer_email, "Verify your email")) == 1)
    customer, admin = login(customer_email), login(admin_email)
    me = expect("who am I", "GET", "/api/auth/me", 200, token=customer)
    expect("wrong password", "POST", "/api/auth/login", 401, {"email": customer_email, "password": "nope"})

    # --- Catalog and Quotes (Quotes asks Catalog over HTTP)
    plans = expect("list plans", "GET", "/api/policies", 200, token=customer)
    check("the catalog is seeded", len(plans) >= 20, f"{len(plans)} plans")
    auto = next(p for p in plans if p["policyType"] == "Auto")
    answers = answers_for(auto["policyId"], customer)
    quote = expect("get a quote", "POST", "/api/quotes", 201, {"policyId": auto["policyId"], "answers": answers}, customer)

    # --- Email verification: browsing and quotes are fine, enrolling needs a verified address
    expect("enrolling before verifying", "POST", "/api/enrollments", 403, {"quoteId": quote["quoteId"]}, customer)
    verify_email(customer_email)

    # --- Enrollments: the saga (Quotes -> Enrollments -> RabbitMQ -> Billing -> RabbitMQ -> Enrollments)
    policy = expect("enroll", "POST", "/api/enrollments", 201, {"quoteId": quote["quoteId"], "startDate": today}, customer)
    check("the saga activated the policy", policy["status"] == "Active", policy["status"])
    check("Billing created one installment per month", policy["paymentsTotal"] == auto["termMonths"],
          f"{policy['paymentsTotal']} installments for a {auto['termMonths']}-month term")
    expect("a quote can only be used once", "POST", "/api/enrollments", 409, {"quoteId": quote["quoteId"]}, customer)
    expect("missing quote", "POST", "/api/enrollments", 404, {"quoteId": 999999999}, customer)

    # --- Billing (asks Enrollments who owns the policy)
    eid = policy["enrollmentId"]
    schedule = expect("premium schedule", "GET", f"/api/enrollments/{eid}/payments", 200, token=customer)
    def card(token, key=None):   # what the checkout page sends: a token, never a card number
        return {"paymentMethodToken": token, "idempotencyKey": key or f"e2e-{RUN}-{time.time_ns()}"}
    first, second = schedule[0]["paymentId"], schedule[1]["paymentId"]
    expect("pay out of order", "POST", f"/api/payments/{second}/pay", 409, card("tok_mock_card_visa_4242_1230"), customer)
    declined = expect("declined card", "POST", f"/api/payments/{first}/pay", 200, card("tok_mock_card_visa_0002_1230"), customer)
    check("a decline is reported, not charged", declined["outcome"] == "Declined", declined["outcome"])
    paid = expect("pay the first installment", "POST", f"/api/payments/{first}/pay", 200,
                  card("tok_mock_card_visa_4242_1230", key=f"e2e-{RUN}-pay1"), customer)
    check("the installment is paid", paid["payment"]["status"] == "Paid", paid["payment"]["status"])
    retry = expect("retry with the same key", "POST", f"/api/payments/{first}/pay", 200,
                   card("tok_mock_card_visa_4242_1230", key=f"e2e-{RUN}-pay1"), customer)
    check("a retry replays the receipt", retry["transaction"]["transactionId"] == paid["transaction"]["transactionId"], retry)
    expect("pay it twice", "POST", f"/api/payments/{first}/pay", 409, card("tok_mock_card_visa_4242_1230"), customer)
    check("one declined-payment email", len(emails_to(customer_email, "didn't go through")) == 1)
    check("one receipt email (the retry sent none)", len(emails_to(customer_email, "Payment receipt")) == 1)
    log = expect("payment history", "GET", f"/api/payments/transactions?enrollmentId={eid}", 200, token=customer)
    check("the history has the decline and the payment", [t["status"] for t in log] == ["Succeeded", "Declined"], log)

    # --- Claims (asks Enrollments, Billing, Catalog and Identity)
    claim_body = {"enrollmentId": eid, "incidentDate": today, "description": "Rear-ended at a light", "claimAmount": 500}
    claim = expect("file a claim", "POST", "/api/claims", 201, claim_body, customer)
    check("the claim shows the customer's name (from Identity)", claim["customerName"] == me["fullName"], claim["customerName"])
    expect("claim above the coverage", "POST", "/api/claims", 400, {**claim_body, "claimAmount": 999999999}, customer)
    expect("customers can't review claims", "PUT", f"/api/claims/{claim['claimId']}/review", 403, {"status": "UnderReview"}, customer)
    expect("start the review", "PUT", f"/api/claims/{claim['claimId']}/review", 200, {"status": "UnderReview"}, admin)
    approved = expect("approve", "PUT", f"/api/claims/{claim['claimId']}/review", 200, {"status": "Approved"}, admin)
    check("the reviewer's name is shown", approved["reviewedBy"] == "Ada Tester", approved["reviewedBy"])

    # --- Gateway BFF: one screen composed from Identity, Enrollments and Claims
    rows = expect("admin customer list", "GET", f"/api/admin/customers?search={customer_email}", 200, token=admin)
    row = rows[0] if rows else {}
    check("counts come from three services", (row.get("activePolicies"), row.get("totalClaims")) == (1, 1), row)

    # --- Gateway: a deactivated account is refused on its very next request
    expect("deactivate", "PUT", f"/api/admin/customers/{me['userId']}/active", 200, {"isActive": False}, admin)
    expect("deactivated token refused", "GET", "/api/auth/me", 401, token=customer)
    expect("reactivate", "PUT", f"/api/admin/customers/{me['userId']}/active", 200, {"isActive": True}, admin)
    expect("token works again", "GET", "/api/auth/me", 200, token=customer)

    # --- Cancelling (the outbox publishes EnrollmentCancelled; Billing removes unpaid installments)
    expect("cancel", "PUT", f"/api/enrollments/{eid}/cancel", 204, token=customer)
    after = expect("schedule after cancelling", "GET", f"/api/enrollments/{eid}/payments", 200, token=customer)
    check("only the paid installment is left", [p["status"] for p in after] == ["Paid"], after)
    quote2 = expect("new quote", "POST", "/api/quotes", 201, {"policyId": auto["policyId"], "answers": answers}, customer)
    expect("enroll again after cancelling", "POST", "/api/enrollments", 201, {"quoteId": quote2["quoteId"]}, customer)

    # --- Internal endpoints are not reachable from outside
    expect("/internal is not routed", "GET", "/internal/plans?ids=1", 404, token=customer)

    print(f"\n{'FAILED: ' + str(len(failures)) if failures else 'All checks passed.'}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:   # a failed step left nothing for the next one to use
        print(f"\nSTOPPED after {len(failures)} failure(s): {error!r}")
        sys.exit(1)
