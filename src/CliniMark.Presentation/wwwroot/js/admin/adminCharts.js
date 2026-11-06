// Pure data shaping for the admin charts (no DOM), shared with the node tests.
// The server sends hourly UTC buckets; grouping happens here so days follow the admin's own time zone.
(function (root) {
  "use strict";
  const pad = n => String(n).padStart(2, "0");
  const dayKey = date => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  const dayLabel = date => date.toLocaleDateString(undefined, { day: "numeric", month: "short" });

  function startOfDay(date) {
    const copy = new Date(date.getTime());
    copy.setHours(0, 0, 0, 0);
    return copy;
  }

  function daily(buckets, now) {
    const end = startOfDay(now || new Date());
    if (!buckets.length) return { labels: [dayLabel(end)], keys: [dayKey(end)], specialist: [0], student: [0] };
    const totals = new Map();
    let first = end;
    for (const bucket of buckets) {
      const date = new Date(bucket.hour);
      const key = dayKey(date);
      const day = startOfDay(date);
      if (day < first) first = day;
      const total = totals.get(key) || { specialist: 0, student: 0 };
      total.specialist += bucket.specialist;
      total.student += bucket.student;
      totals.set(key, total);
    }
    const result = { labels: [], keys: [], specialist: [], student: [] };
    for (let day = first; day <= end; day = new Date(day.getFullYear(), day.getMonth(), day.getDate() + 1)) {
      const key = dayKey(day);
      const total = totals.get(key) || { specialist: 0, student: 0 };
      result.labels.push(dayLabel(day));
      result.keys.push(key);
      result.specialist.push(total.specialist);
      result.student.push(total.student);
    }
    return result;
  }

  function lastHours(buckets, now, hours) {
    const span = hours || 48;
    const end = new Date((now || new Date()).getTime());
    end.setMinutes(0, 0, 0);
    const byHour = new Map(buckets.map(bucket => [new Date(bucket.hour).getTime(), bucket]));
    const result = { labels: [], specialist: [], student: [] };
    for (let index = span - 1; index >= 0; index--) {
      const hour = new Date(end.getTime() - index * 3600000);
      const bucket = byHour.get(hour.getTime());
      result.labels.push(hour.getHours() === 0 ? `${dayLabel(hour)} 00:00` : `${pad(hour.getHours())}:00`);
      result.specialist.push(bucket ? bucket.specialist : 0);
      result.student.push(bucket ? bucket.student : 0);
    }
    return result;
  }

  function cumulative(buckets, now) {
    const days = daily(buckets, now);
    let specialist = 0, student = 0;
    return {
      labels: days.labels,
      specialist: days.specialist.map(value => (specialist += value)),
      student: days.student.map(value => (student += value))
    };
  }

  function categorySeries(items, role) {
    const rows = (items || []).map(item => ({ value: item.value, ...item[role] }))
      .map(row => ({ ...row, total: row.done + row.active + row.open }))
      .filter(row => row.total > 0)
      .sort((a, b) => b.total - a.total || a.value.localeCompare(b.value));
    return {
      labels: rows.map(row => row.value),
      done: rows.map(row => row.done),
      active: rows.map(row => row.active),
      open: rows.map(row => row.open)
    };
  }

  const api = { daily, lastHours, cumulative, categorySeries, dayKey };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  root.AdminCharts = api;
})(typeof window !== "undefined" ? window : globalThis);
