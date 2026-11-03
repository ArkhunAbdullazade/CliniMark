// requestRetry.js: saves survive an app restart (502/503/504 or no connection) and are never repeated otherwise.
const test = require("node:test");
const assert = require("node:assert/strict");
const { fetchWithRetry, DELAYS_MS, UNREACHABLE } = require("../../src/MedAnnotateApp.Presentation/wwwroot/js/requestRetry.js");

const respond = status => ({ status, ok: status >= 200 && status < 300 });
const noSleep = () => Promise.resolve();

function scripted(outcomes) {
  const calls = [];
  const fetchImpl = async (url, options) => {
    calls.push({ url, options });
    const next = outcomes[Math.min(calls.length - 1, outcomes.length - 1)];
    if (next instanceof Error) throw next;
    return respond(next);
  };
  return { calls, fetchImpl };
}

test("a save during a restart is repeated until the new version answers", async () => {
  const { calls, fetchImpl } = scripted([503, new TypeError("Failed to fetch"), 502, 504, 200]);
  const retries = [];
  const response = await fetchWithRetry("/MedData/ProcessAnnotatedMedData", { method: "POST", body: "{}" },
    { fetchImpl, sleep: noSleep, onRetry: (attempt, wait) => retries.push([attempt, wait]) });
  assert.equal(response.status, 200);
  assert.equal(calls.length, 5);
  assert.ok(calls.every(call => call.options.body === "{}" && call.options.method === "POST"));
  assert.deepEqual(retries, [[1, 1000], [2, 2000], [3, 3000], [4, 5000]]);
});

test("answers from the app itself are returned at once, never repeated", async () => {
  for (const status of [200, 400, 401, 403, 404, 409, 429, 500]) {
    const { calls, fetchImpl } = scripted([status, 200]);
    const response = await fetchWithRetry("/x", {}, { fetchImpl, sleep: noSleep, onRetry: () => assert.fail("no retry") });
    assert.equal(response.status, status);
    assert.equal(calls.length, 1);
  }
});

test("waits about 80 seconds in total before giving up on a gateway error", async () => {
  const slept = [];
  const { calls, fetchImpl } = scripted([503]);
  const response = await fetchWithRetry("/x", {}, { fetchImpl, sleep: ms => { slept.push(ms); return Promise.resolve(); } });
  assert.equal(response.status, 503);
  assert.equal(calls.length, DELAYS_MS.length + 1);
  assert.deepEqual(slept, DELAYS_MS);
  const total = slept.reduce((a, b) => a + b, 0);
  assert.ok(total >= 60000 && total <= 120000, `total wait ${total} ms`);
});

test("no connection at all ends with a message that the work is still on the page", async () => {
  const { fetchImpl } = scripted([new TypeError("Failed to fetch")]);
  await assert.rejects(fetchWithRetry("/x", {}, { fetchImpl, sleep: noSleep, delays: [1, 1] }),
    error => error.message === UNREACHABLE && error.status === 0 && error.cause instanceof TypeError);
});
