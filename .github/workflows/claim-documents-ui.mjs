// TEMPORARY: drives the real Angular app in a browser. Removed before the pull request is opened.
import { chromium } from 'playwright';
import { writeFileSync, readFileSync } from 'node:fs';

const APP = 'http://localhost:4200', API = 'http://localhost:5080/api';
const PASS = 'Demo#2026pass';
writeFileSync('estimate.pdf', '%PDF-1.7\nfrom the browser\n');
writeFileSync('notes.txt', 'not allowed');

let fails = 0;
const check = (name, ok, extra = '') => { console.log(`${ok ? 'PASS' : 'FAIL'}  ${name} ${extra}`); if (!ok) fails++; };

async function token(email, password) {
  const r = await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) });
  return (await r.json()).token;
}
const seen = (l) => l.waitFor({ timeout: 10000 }).then(() => true, () => false);
const browser = await chromium.launch();

async function session(email, password) {
  const ctx = await browser.newContext({ acceptDownloads: true });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { console.log('PAGE ERROR', e.message); fails++; });
  await page.goto(`${APP}/login`);
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL(/dashboard/);
  return page;
}

const emmaToken = await token('emma.carter@demo.example', PASS);
const list = await (await fetch(`${API}/enrollments/mine`, { headers: { Authorization: `Bearer ${emmaToken}` } })).json();
const enrollment = list.find((e) => e.status === 'Active' && e.paymentAlert !== 'Overdue' && e.startDate);
console.log('Enrollment', enrollment.enrollmentId);

async function fileClaim(page, amount) {
  await page.goto(`${APP}/my-policies/${enrollment.enrollmentId}/claims/new`);
  const date = page.getByLabel('Date of incident');
  await date.waitFor();
  await date.fill(await date.getAttribute('max'));
  await page.getByLabel('Claim amount').fill(String(amount));
  await page.getByLabel('What happened?').fill('A pipe burst and damaged the floor.');
}

const emma = await session('emma.carter@demo.example', PASS);

// A wrong file type is caught in the form.
await fileClaim(emma, 11);
const picker = emma.getByLabel('Supporting documents (optional)');
await picker.setInputFiles('notes.txt');
check('form refuses a text file', await seen(emma.getByText("notes.txt isn't a PDF, JPG or PNG file.")));
await picker.setInputFiles('estimate.pdf');
check('form lists the chosen file', await seen(emma.getByRole('button', { name: 'Remove estimate.pdf' })));

// Filing with a document.
await emma.getByRole('button', { name: 'Submit claim' }).click();
await emma.waitForURL(/\/claims$/);
const row = emma.getByRole('row').filter({ hasText: 'Submitted' }).first();
await emma.getByRole('button', { name: 'Documents' }).first().click();
await emma.getByText('estimate.pdf').waitFor();
check('document shows on My claims', true);
const [download] = await Promise.all([emma.waitForEvent('download'), emma.getByRole('button', { name: 'Download estimate.pdf' }).click()]);
check('download keeps its name', download.suggestedFilename() === 'estimate.pdf', download.suggestedFilename());
check('download has the right bytes', readFileSync(await download.path(), 'utf8').includes('from the browser'));

// A failed upload doesn't invite a second claim.
await fileClaim(emma, 12);
await emma.route('**/documents', (route) => route.request().method() === 'POST' ? route.abort() : route.continue());
await emma.getByLabel('Supporting documents (optional)').setInputFiles('estimate.pdf');
await emma.getByRole('button', { name: 'Submit claim' }).click();
await emma.waitForURL(/documentsFailed=/);
check('failed upload explains itself', await seen(emma.getByText('You don\'t need to file the claim again.')));
await emma.unroute('**/documents');
await emma.getByLabel('Add documents').setInputFiles('estimate.pdf');
await emma.getByText('estimate.pdf').waitFor();
check('customer can retry the upload from My claims', true);

// Admin review.
const admin = await session('admin@check.example', 'Adm1n#Check2026');
await admin.goto(`${APP}/admin/claims`);
await admin.getByRole('row').filter({ hasText: 'Emma Carter' }).filter({ hasText: 'Submitted' }).first().getByRole('button', { name: 'Open' }).click();
await admin.getByRole('button', { name: 'Start review' }).click();
await admin.getByRole('button', { name: 'Request more info' }).waitFor();
check('admin sees the new action', true);
await admin.getByRole('button', { name: 'Request more info' }).click();
check('note is required', await seen(admin.getByText('Please tell the customer what information you need.')));
await admin.getByLabel(/Notes to the customer/).fill('Please upload the repair estimate.');
await admin.getByRole('button', { name: 'Request more info' }).click();
await admin.locator('.review-panel .badge', { hasText: 'Needs more info' }).waitFor({ timeout: 10000 });
check('admin sees Needs more info', true);
check('approve is no longer offered', (await admin.getByRole('button', { name: 'Approve' }).count()) === 0);
check('admin can resume or reject', await seen(admin.getByRole('button', { name: 'Start review' }).isVisible() && await admin.getByRole('button', { name: 'Reject' })));
check('admin sees the claim documents', await seen(admin.locator('app-claim-documents')));

// The customer responds.
await emma.goto(`${APP}/claims`);
const send = emma.getByRole('button', { name: 'Send to reviewer' });
await send.waitFor();
check('customer sees the reviewer note', await seen(emma.getByText('Note from reviewer: Please upload the repair estimate.')));
await send.click();
await send.waitFor({ state: 'detached' });
check('claim went back to review', true);

await browser.close();
console.log(`Failures: ${fails}`);
process.exit(fails);
