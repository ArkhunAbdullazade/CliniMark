(function () {
  "use strict";
  const source = document.getElementById("overview-data");
  if (!source || !window.Chart) return;
  const data = JSON.parse(source.textContent);
  const css = getComputedStyle(document.documentElement);
  const color = name => css.getPropertyValue(name).trim();
  const SPECIALIST = color("--series-specialist"), STUDENT = color("--series-student");
  const DONE = color("--progress-done"), ACTIVE = color("--progress-active"), OPEN = color("--progress-open");
  const INK = color("--ink"), LINE = color("--line-soft");
  const MIX = { "Saved": SPECIALIST, "Uncertain": STUDENT, "Not visible": color("--ok") };

  Chart.defaults.font.family = color("--font");
  Chart.defaults.font.size = 12;
  Chart.defaults.color = color("--muted");
  Chart.defaults.borderColor = LINE;
  Chart.defaults.plugins.legend.labels.boxWidth = 10;
  Chart.defaults.plugins.legend.labels.boxHeight = 10;
  Chart.defaults.plugins.legend.labels.useBorderRadius = true;
  Chart.defaults.plugins.legend.labels.borderRadius = 1;
  Chart.defaults.plugins.tooltip.backgroundColor = INK;
  Chart.defaults.plugins.tooltip.padding = 10;
  Chart.defaults.plugins.tooltip.cornerRadius = 4;
  Chart.defaults.maintainAspectRatio = false;

  const bar = (label, values, background, extra) => ({
    label, data: values, backgroundColor: background, borderRadius: 1, borderSkipped: "start",
    maxBarThickness: 28, borderWidth: 0, ...extra
  });
  const integerAxis = { beginAtZero: true, ticks: { precision: 0 }, grid: { color: LINE }, border: { display: false } };
  const plainAxis = { grid: { display: false }, border: { color: color("--line") } };

  // Activity: grouped bars per local day, or the last 48 hours.
  const activityCanvas = document.getElementById("activity-chart");
  const activityChart = new Chart(activityCanvas, {
    type: "bar",
    data: { labels: [], datasets: [] },
    options: {
      interaction: { mode: "index", intersect: false },
      plugins: { legend: { position: "top", align: "end" } },
      scales: { x: { ...plainAxis, ticks: { autoSkip: true, maxRotation: 0 } }, y: integerAxis }
    }
  });
  function showActivity(range) {
    const shaped = range === "hours" ? AdminCharts.lastHours(data.activity, new Date(), 48) : AdminCharts.daily(data.activity, new Date());
    activityChart.data.labels = shaped.labels;
    activityChart.data.datasets = [
      bar("Specialist answers", shaped.specialist, SPECIALIST, { stack: range === "hours" ? "all" : undefined }),
      bar("Student groups", shaped.student, STUDENT, { stack: range === "hours" ? "all" : undefined })
    ];
    activityChart.options.scales.x.stacked = range === "hours";
    activityChart.options.scales.y.stacked = range === "hours";
    activityChart.update();
  }
  document.querySelectorAll("input[name=activity-range]").forEach(input =>
    input.addEventListener("change", () => showActivity(input.value)));
  showActivity("days");

  // Response mix: one bar per response, direct-labelled.
  const mixTotal = data.mix.reduce((sum, item) => sum + item.count, 0);
  new Chart(document.getElementById("mix-chart"), {
    type: "bar",
    data: {
      labels: data.mix.map(item => item.label),
      datasets: [bar("Answers", data.mix.map(item => item.count), data.mix.map(item => MIX[item.label] || color("--muted")), { maxBarThickness: 36 })]
    },
    options: {
      indexAxis: "y",
      plugins: {
        legend: { display: false },
        tooltip: { callbacks: { label: context => ` ${context.parsed.x} answers (${mixTotal ? Math.round(context.parsed.x * 100 / mixTotal) : 0}%)` } }
      },
      // Headroom on the right keeps the direct labels inside the chart.
      scales: { x: { ...integerAxis, grace: "25%" }, y: { ...plainAxis, ticks: { color: INK } } }
    },
    plugins: [valueLabels(value => `${value} · ${mixTotal ? Math.round(value * 100 / mixTotal) : 0}%`)]
  });

  // Progress by category: stacked horizontal bars on a one-hue ramp (done darkest).
  const categoryCanvas = document.getElementById("category-chart");
  const categoryBox = document.getElementById("category-box");
  const categoryChart = new Chart(categoryCanvas, {
    type: "bar",
    data: { labels: [], datasets: [] },
    options: {
      indexAxis: "y",
      interaction: { mode: "index", intersect: false },
      plugins: { legend: { display: false } },
      scales: { x: { ...integerAxis, stacked: true }, y: { ...plainAxis, stacked: true, ticks: { color: INK, autoSkip: false } } }
    }
  });
  function showCategories() {
    const dimension = document.getElementById("category-dimension").value;
    const role = document.querySelector("input[name=category-role]:checked").value;
    const shaped = AdminCharts.categorySeries(data.categories[dimension], role);
    categoryBox.style.height = `${Math.max(220, shaped.labels.length * 30 + 60)}px`;
    categoryChart.data.labels = shaped.labels.map(label => label.charAt(0).toUpperCase() + label.slice(1));
    // 2px surface-coloured borders give the gaps between stacked fills.
    const segment = (label, values, fill) => bar(label, values, fill, { borderWidth: { right: 2 }, borderColor: "#ffffff", borderSkipped: false, borderRadius: 0, maxBarThickness: 20 });
    categoryChart.data.datasets = [segment("Done", shaped.done, DONE), segment("In progress", shaped.active, ACTIVE), segment("Open", shaped.open, OPEN)];
    categoryChart.resize();
    categoryChart.update();
  }
  document.getElementById("category-dimension").addEventListener("change", showCategories);
  document.querySelectorAll("input[name=category-role]").forEach(input => input.addEventListener("change", showCategories));
  showCategories();

  // Timing histograms: a single series each, so no legend.
  for (const [id, bins, label] of [["first-draw-chart", data.firstDraw, "Answers"], ["drawing-chart", data.drawing, "Answers"]]) {
    new Chart(document.getElementById(id), {
      type: "bar",
      data: { labels: bins.map(bin => bin.label), datasets: [bar(label, bins.map(bin => bin.count), SPECIALIST, { maxBarThickness: 40 })] },
      options: { plugins: { legend: { display: false } }, scales: { x: plainAxis, y: integerAxis } }
    });
  }

  // Sign-ups: cumulative lines per role.
  const signups = AdminCharts.cumulative(data.signups, new Date());
  const line = (label, values, stroke) => ({
    label, data: values, borderColor: stroke, backgroundColor: stroke, borderWidth: 2, pointRadius: 3, pointHoverRadius: 5,
    pointBorderColor: "#ffffff", pointBorderWidth: 1,
    pointHoverBorderColor: "#ffffff", pointHoverBorderWidth: 2, tension: 0.25, stepped: false
  });
  new Chart(document.getElementById("signup-chart"), {
    type: "line",
    data: { labels: signups.labels, datasets: [line("Specialists", signups.specialist, SPECIALIST), line("Students", signups.student, STUDENT)] },
    options: {
      interaction: { mode: "index", intersect: false },
      plugins: { legend: { position: "top", align: "end" } },
      scales: { x: { ...plainAxis, ticks: { maxRotation: 0, autoSkip: true } }, y: integerAxis }
    }
  });

  function valueLabels(format) {
    return {
      id: "valueLabels",
      afterDatasetsDraw(chart) {
        const { ctx } = chart;
        ctx.save();
        ctx.font = `600 12px ${Chart.defaults.font.family}`;
        ctx.fillStyle = INK;
        ctx.textBaseline = "middle";
        chart.getDatasetMeta(0).data.forEach((element, index) => {
          const value = chart.data.datasets[0].data[index];
          ctx.fillText(format(value), element.x + 8, element.y);
        });
        ctx.restore();
      }
    };
  }
})();
