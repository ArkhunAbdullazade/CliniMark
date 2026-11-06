// Keeps a save alive while the app restarts (a new version going live): the request is repeated while the server
// cannot be reached (no connection) or the platform answers 502/503/504, for about 80 seconds in total. Any other
// answer, including errors, is returned at once. Repeating a save can never store it twice: the server refuses a
// second save of the same term or image ("already has a saved response"), so the saved work is never duplicated.
(function (root) {
  "use strict";
  const RETRY_STATUSES = new Set([502, 503, 504]);
  const DELAYS_MS = [1000, 2000, 3000, 5000, 5000, 10000, 10000, 15000, 15000, 15000];
  const UNREACHABLE = "The server could not be reached. Your work is still on this page: check your connection and save again.";

  async function fetchWithRetry(url, options, settings) {
    const { onRetry, delays = DELAYS_MS, fetchImpl = (...args) => root.fetch(...args),
      sleep = ms => new Promise(resolve => setTimeout(resolve, ms)) } = settings || {};
    for (let attempt = 0; ; attempt++) {
      let response = null, failure = null;
      try { response = await fetchImpl(url, options); } catch (error) { failure = error; }
      const retryable = failure !== null || RETRY_STATUSES.has(response.status);
      if (!retryable) return response;
      if (attempt >= delays.length) {
        if (failure) throw Object.assign(new Error(UNREACHABLE), { cause: failure, status: 0 });
        return response;
      }
      if (onRetry) onRetry(attempt + 1, delays[attempt]);
      await sleep(delays[attempt]);
    }
  }

  const api = { fetchWithRetry, RETRY_STATUSES, DELAYS_MS, UNREACHABLE };
  root.CliniMarkRetry = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
