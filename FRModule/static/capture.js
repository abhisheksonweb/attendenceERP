// Add-student flow: capture face first, then show the details popup.
(function () {
  const captureBtn = document.getElementById("capture-btn");
  if (!captureBtn) return;

  const statusEl = document.getElementById("capture-status");
  const modal = document.getElementById("details-modal");
  const closeBtn = document.getElementById("modal-close");
  const cancelBtn = document.getElementById("modal-cancel");
  const form = document.getElementById("details-form");
  const tokenInput = document.getElementById("capture-token");
  const preview = document.getElementById("face-preview");
  const samplesPill = document.getElementById("samples-pill");
  const dupNote = document.getElementById("duplicate-note");
  const formError = document.getElementById("form-error");
  const saveBtn = document.getElementById("save-btn");

  function setStatus(text, kind) {
    statusEl.textContent = text || "";
    statusEl.className = "capture-status" + (kind ? " capture-" + kind : "");
  }

  function openModal() {
    modal.hidden = false;
  }
  function closeModal() {
    modal.hidden = true;
    form.reset();
    formError.hidden = true;
  }

  closeBtn.addEventListener("click", closeModal);
  cancelBtn.addEventListener("click", closeModal);
  modal.addEventListener("click", (e) => {
    if (e.target === modal) closeModal();
  });

  captureBtn.addEventListener("click", async function () {
    const url = captureBtn.dataset.captureUrl;
    captureBtn.disabled = true;
    const original = captureBtn.textContent;
    captureBtn.textContent = "Capturing...";
    setStatus("Hold still and look at the camera...", "working");

    try {
      const res = await fetch(url, { method: "POST" });
      const data = await res.json();

      if (!data.ok) {
        setStatus(data.error || "Capture failed. Try again.", "error");
        return;
      }

      tokenInput.value = data.token;
      samplesPill.textContent = data.samples + " samples";
      if (data.preview) {
        preview.src = data.preview;
        preview.hidden = false;
      } else {
        preview.hidden = true;
      }

      if (data.duplicate) {
        const d = data.duplicate;
        dupNote.hidden = false;
        dupNote.textContent = d.same_class
          ? `Warning: this face is already enrolled in this class as ${d.name} (${d.roll_no}).`
          : `Note: this face resembles ${d.name} (${d.roll_no}) enrolled in another class.`;
      } else {
        dupNote.hidden = true;
      }

      setStatus("Face verified. Fill in the details.", "ok");
      openModal();
    } catch (e) {
      setStatus("Network error during capture. Try again.", "error");
    } finally {
      captureBtn.disabled = false;
      captureBtn.textContent = original;
    }
  });

  form.addEventListener("submit", async function (e) {
    e.preventDefault();
    formError.hidden = true;
    saveBtn.disabled = true;
    const original = saveBtn.textContent;
    saveBtn.textContent = "Saving...";

    try {
      const res = await fetch(form.dataset.submitUrl, {
        method: "POST",
        body: new FormData(form),
      });
      const data = await res.json();
      if (data.ok) {
        window.location.href = data.redirect;
        return;
      }
      formError.hidden = false;
      formError.textContent = data.error || "Could not save student.";
    } catch (e) {
      formError.hidden = false;
      formError.textContent = "Network error. Try again.";
    } finally {
      saveBtn.disabled = false;
      saveBtn.textContent = original;
    }
  });
})();
