#!/usr/bin/env bash
# TEMPORARY: exercises the real system through the gateway. Removed before the pull request is opened.
set -uo pipefail
API=http://localhost:5080/api
COMPOSE="docker compose -f deploy/docker-compose.yml"
WORK=$(mktemp -d); FAILS=0

check() { # name expected actual
  if [ "$2" == "$3" ]; then echo "PASS  $1"; else echo "FAIL  $1: expected $2, got $3"; FAILS=$((FAILS+1)); fi
}
sql() { $COMPOSE exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SA_PASSWORD" "$@"; }
login() { curl -s -X POST $API/auth/login -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"$2\"}" | jq -r .token; }
code() { # token method path [curl args...]
  local t=$1 m=$2 p=$3; shift 3
  curl -s -o "$WORK/body" -w '%{http_code}' -X "$m" -H "Authorization: Bearer $t" "$API$p" "$@"
}
upload() { code "$1" POST "/claims/$2/documents" -F "file=@$3;filename=${4:-$(basename "$3")}"; }

echo "Waiting for the system..."
ADMIN_BODY='{"firstName":"Ada","lastName":"Admin","email":"admin@check.example","password":"Adm1n#Check2026","street":"1 Main St","city":"Boston","state":"MA","postalCode":"02101"}'
for i in $(seq 1 90); do
  s=$(curl -s -o "$WORK/body" -w '%{http_code}' -X POST $API/auth/register -H 'Content-Type: application/json' -d "$ADMIN_BODY")
  [ "$s" == "201" ] || [ "$s" == "200" ] && break
  sleep 3
done
echo "register: $s $(head -c 300 "$WORK/body")"
curl -s -f http://localhost:5080/health >/dev/null; check "gateway healthy" 0 $?
sql -Q "UPDATE InsuranceIdentity.accounts.Users SET Role='Admin' WHERE Email='admin@check.example'"
sql -i /seed/demo-data.sql | tail -3

ADMIN=$(login admin@check.example 'Adm1n#Check2026')
EMMA=$(login emma.carter@demo.example 'Demo#2026pass')
MARCUS=$(login marcus.johnson@demo.example 'Demo#2026pass')

curl -s -H "Authorization: Bearer $EMMA" $API/claims/mine > "$WORK/mine.json"
U=$(jq -r '[.[]|select(.status=="UnderReview")][0].claimId' "$WORK/mine.json")
A=$(jq -r '[.[]|select(.status=="Approved")][0].claimId' "$WORK/mine.json")
echo "Under review claim: $U, approved claim: $A"
check "customer can add documents while under review" true "$(jq -r "map(select(.claimId==$U))[0].canAddDocuments" "$WORK/mine.json")"
check "customer can't add documents once approved" false "$(jq -r "map(select(.claimId==$A))[0].canAddDocuments" "$WORK/mine.json")"

printf '%%PDF-1.7\nclaim evidence\n' > "$WORK/estimate.pdf"
printf '\x89PNG\r\n\x1a\nphoto' > "$WORK/photo.png"
printf '<script>alert(1)</script>' > "$WORK/fake.pdf"
(printf '%%PDF-'; head -c 10485760 /dev/zero) > "$WORK/big.pdf"
: > "$WORK/empty.pdf"

check "upload a PDF" 201 "$(upload "$EMMA" "$U" "$WORK/estimate.pdf")"
DOC=$(jq -r .claimDocumentId "$WORK/body")
check "no storage key in the response" null "$(jq -r .storageKey "$WORK/body")"
check "upload a PNG" 201 "$(upload "$EMMA" "$U" "$WORK/photo.png" 'renamed.txt')"
check "PNG type detected from contents" image/png "$(jq -r .contentType "$WORK/body")"
check "a script named .pdf is refused" 400 "$(upload "$EMMA" "$U" "$WORK/fake.pdf")"
check "a file over 10 MB is refused" 400 "$(upload "$EMMA" "$U" "$WORK/big.pdf")"
echo "      over-limit message: $(jq -r .title "$WORK/body")"
check "an empty file is refused" 400 "$(upload "$EMMA" "$U" "$WORK/empty.pdf")"
check "an upload with no file is refused" 400 "$(code "$EMMA" POST "/claims/$U/documents" -F 'other=1')"
check "no upload to an approved claim" 409 "$(upload "$EMMA" "$A" "$WORK/estimate.pdf")"
check "no upload to another customer's claim" 404 "$(upload "$MARCUS" "$U" "$WORK/estimate.pdf")"
check "no list of another customer's claim" 404 "$(code "$MARCUS" GET "/claims/$U/documents")"
check "no download from another customer's claim" 404 "$(code "$MARCUS" GET "/claims/$U/documents/$DOC")"
check "no anonymous download" 401 "$(curl -s -o /dev/null -w '%{http_code}' "$API/claims/$U/documents/$DOC")"
check "a document isn't reachable through another claim" 404 "$(code "$EMMA" GET "/claims/$A/documents/$DOC")"
check "admins can't upload" 403 "$(upload "$ADMIN" "$U" "$WORK/estimate.pdf")"

check "customer lists documents" 200 "$(code "$EMMA" GET "/claims/$U/documents")"
check "two documents listed" 2 "$(jq length "$WORK/body")"
curl -s -D "$WORK/headers" -o "$WORK/down.pdf" -H "Authorization: Bearer $ADMIN" "$API/claims/$U/documents/$DOC"
cmp -s "$WORK/estimate.pdf" "$WORK/down.pdf"; check "admin downloads the exact bytes" 0 $?
check "served as an attachment" 1 "$(grep -i -c '^content-disposition: attachment' "$WORK/headers")"
check "nosniff is set" 1 "$(grep -i -c '^x-content-type-options: nosniff' "$WORK/headers")"

JSON=(-H 'Content-Type: application/json')
check "asking for more needs a note" 400 "$(code "$ADMIN" PUT "/claims/$U/review" "${JSON[@]}" -d '{"status":"NeedsMoreInfo"}')"
check "admin asks for more" 200 "$(code "$ADMIN" PUT "/claims/$U/review" "${JSON[@]}" -d '{"status":"NeedsMoreInfo","adminNotes":"Please upload the repair estimate."}')"
check "admin's next steps" 'UnderReview,Rejected' "$(jq -r '.allowedNextStatuses|join(",")' "$WORK/body")"
code "$EMMA" GET "/claims/$U" >/dev/null
check "customer sees the status" NeedsMoreInfo "$(jq -r .status "$WORK/body")"
check "customer sees the note" 'Please upload the repair estimate.' "$(jq -r .adminNotes "$WORK/body")"
check "customer can send it back" true "$(jq -r .canSendToReviewer "$WORK/body")"
check "the customer list still counts it as open" 200 "$(code "$ADMIN" GET '/admin/customers?search=emma')"
check "counted as open" true "$(jq -r '[.[]|.openClaims]|max > 0' "$WORK/body")"
check "customer uploads while it's waiting" 201 "$(upload "$EMMA" "$U" "$WORK/estimate.pdf")"
check "another customer can't send it back" 404 "$(code "$MARCUS" POST "/claims/$U/send-to-reviewer")"
check "customer sends it back" 200 "$(code "$EMMA" POST "/claims/$U/send-to-reviewer")"
check "back under review" UnderReview "$(jq -r .status "$WORK/body")"
check "sending it back twice is a conflict" 409 "$(code "$EMMA" POST "/claims/$U/send-to-reviewer")"

check "fourth document" 201 "$(upload "$EMMA" "$U" "$WORK/estimate.pdf")"
check "fifth document" 201 "$(upload "$EMMA" "$U" "$WORK/estimate.pdf")"
check "sixth document is refused" 409 "$(upload "$EMMA" "$U" "$WORK/estimate.pdf")"
echo "      limit message: $(jq -r .title "$WORK/body")"
check "the refused file left nothing behind" 5 "$($COMPOSE exec -T claims sh -c 'ls /data/claim-documents | wc -l' | tr -d '[:space:]')"

$COMPOSE up -d --force-recreate --no-deps claims
for i in $(seq 1 40); do
  s=$(code "$ADMIN" GET "/claims/$U/documents/$DOC"); [ "$s" == "200" ] && break; sleep 3
done
check "file survives recreating the container" 200 "$s"
cmp -s "$WORK/estimate.pdf" "$WORK/body"; check "same bytes after recreating" 0 $?

check "admin can approve afterwards" 200 "$(code "$ADMIN" PUT "/claims/$U/review" "${JSON[@]}" -d '{"status":"Approved"}')"
check "no uploads after approval" 409 "$(upload "$EMMA" "$U" "$WORK/estimate.pdf")"

sql -i /seed/remove-demo-data.sql >/dev/null; check "demo data can still be removed" 0 $?

echo; echo "Failures: $FAILS"
exit $FAILS
