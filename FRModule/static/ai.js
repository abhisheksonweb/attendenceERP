// Attendance assistant: natural-language Q&A + one-click report.
(function () {
  const card = document.querySelector(".ai-card");
  if (!card) return;

  const askEndpoint = card.dataset.askEndpoint;
  const reportEndpoint = card.dataset.reportEndpoint;
  const chat = card.querySelector("#ai-chat");
  const form = card.querySelector("#ai-form");
  const input = card.querySelector("#ai-question");
  const reportBtn = card.querySelector("#ai-report-btn");

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({
      "&": "&amp;",
      "<": "&lt;",
      ">": "&gt;",
      '"': "&quot;",
      "'": "&#39;",
    }[c]));
  }

  function bubble(text, role, source) {
    const el = document.createElement("div");
    el.className = `ai-msg ai-msg-${role}`;
    const html = escapeHtml(text).replace(/\n/g, "<br>");
    let tag = "";
    if (role === "bot" && source) {
      const label = source === "ai" ? "assistant" : "local";
      tag = `<span class="ai-tag">${label}</span>`;
    }
    el.innerHTML = `${html}${tag}`;
    chat.appendChild(el);
    chat.scrollTop = chat.scrollHeight;
    return el;
  }

  async function postJson(url, body) {
    const res = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body || {}),
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  }

  async function ask(question) {
    if (!question) return;
    bubble(question, "user");
    input.value = "";
    const pending = bubble("Thinking…", "bot");
    try {
      const data = await postJson(askEndpoint, { question });
      pending.remove();
      bubble(data.answer || "No answer.", "bot", data.source);
    } catch (e) {
      pending.remove();
      bubble("Sorry, something went wrong reaching the assistant.", "bot");
    }
  }

  form.addEventListener("submit", (e) => {
    e.preventDefault();
    ask(input.value.trim());
  });

  card.querySelectorAll(".chip").forEach((chip) => {
    chip.addEventListener("click", () => ask(chip.dataset.q));
  });

  reportBtn.addEventListener("click", async () => {
    reportBtn.disabled = true;
    const pending = bubble("Generating report…", "bot");
    try {
      const data = await postJson(reportEndpoint, {});
      pending.remove();
      bubble(data.report || "No report.", "bot", data.source);
    } catch (e) {
      pending.remove();
      bubble("Sorry, could not generate the report.", "bot");
    } finally {
      reportBtn.disabled = false;
    }
  });

  // Per-student report buttons in the Enrolled Students table.
  document.querySelectorAll(".js-student-report").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const endpoint = btn.dataset.endpoint;
      const name = btn.dataset.name || "student";
      if (!endpoint) return;
      card.scrollIntoView({ behavior: "smooth", block: "start" });
      bubble(`Generate a report for ${name}.`, "user");
      const pending = bubble("Generating report…", "bot");
      btn.disabled = true;
      try {
        const data = await postJson(endpoint, {});
        pending.remove();
        bubble(data.report || "No report.", "bot", data.source);
      } catch (e) {
        pending.remove();
        bubble("Sorry, could not generate that student's report.", "bot");
      } finally {
        btn.disabled = false;
      }
    });
  });
})();
