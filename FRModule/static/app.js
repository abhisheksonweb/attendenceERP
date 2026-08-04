// Poll today's attendance and refresh the table without reloading the page.
(function () {
  const table = document.getElementById("attendance-table");
  if (!table) return;
  const tbody = table.querySelector("tbody");
  const endpoint = table.dataset.endpoint || "/api/attendance";

  function statusPill(status) {
    const cls = status.toLowerCase() === "out" ? "status-out" : "status-in";
    return `<span class="status ${cls}">${status}</span>`;
  }

  function render(records) {
    if (!records.length) {
      tbody.innerHTML =
        '<tr class="empty-row"><td colspan="8">No attendance recorded yet today.</td></tr>';
      return;
    }
    tbody.innerHTML = records
      .map(
        (r) => `
        <tr>
          <td>${escapeHtml(r.name)}</td>
          <td>${escapeHtml(r.roll_no)}</td>
          <td>${escapeHtml(r.sessions)}</td>
          <td>${escapeHtml(r.first_in)}</td>
          <td>${escapeHtml(r.last_out)}</td>
          <td>${escapeHtml(r.time_in_class)}</td>
          <td>${escapeHtml(r.avg_per_day)}</td>
          <td>${statusPill(r.status)}</td>
        </tr>`
      )
      .join("");
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({
      "&": "&amp;",
      "<": "&lt;",
      ">": "&gt;",
      '"': "&quot;",
      "'": "&#39;",
    }[c]));
  }

  async function refresh() {
    try {
      const res = await fetch(endpoint, { cache: "no-store" });
      if (res.ok) render(await res.json());
    } catch (e) {
      /* transient network error; keep last render */
    }
  }

  refresh();
  setInterval(refresh, 3000);
})();
