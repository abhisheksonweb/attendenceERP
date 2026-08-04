(function ($) {
  'use strict';

  // Dummy cascading lists for Department → Course → Semester
  window.McAcademicTree = {
    'School of Computing': {
      'Btech': ['1', '2', '3', '4', '5', '6', '7', '8']
    },
    'Medicine': {
      'MBBS': ['1', '2', '3', '4', '5', '6', '7', '8']
    }
  };

  window.initDeptCourseSemester = function (deptSelector, courseSelector, semesterSelector) {
    var deptEl = document.querySelector(deptSelector);
    var courseEl = document.querySelector(courseSelector);
    var semesterEl = document.querySelector(semesterSelector);
    if (!deptEl || !courseEl || !semesterEl) return;

    var selectedDept = deptEl.getAttribute('data-selected') || deptEl.value || '';
    var selectedCourse = courseEl.getAttribute('data-selected') || courseEl.value || '';
    var selectedSemester = semesterEl.getAttribute('data-selected') || semesterEl.value || '';
    var tree = window.McAcademicTree;

    function fillOptions(select, items, placeholder, selected) {
      select.innerHTML = '';
      var ph = document.createElement('option');
      ph.value = '';
      ph.textContent = placeholder;
      select.appendChild(ph);
      (items || []).forEach(function (item) {
        var opt = document.createElement('option');
        opt.value = item;
        opt.textContent = /^\d+$/.test(item) ? ('Semester ' + item) : item;
        if (selected && String(selected) === String(item)) opt.selected = true;
        select.appendChild(opt);
      });
      select.disabled = !(items && items.length);
    }

    function refreshCourses(keepCourse) {
      var dept = deptEl.value;
      var courses = dept && tree[dept] ? Object.keys(tree[dept]) : [];
      var courseVal = keepCourse ? (courseEl.value || selectedCourse) : '';
      fillOptions(courseEl, courses, '-- Select course --', courseVal);
      if (!courses.includes(courseEl.value)) courseEl.value = '';
      refreshSemesters(keepCourse);
    }

    function refreshSemesters(keepSemester) {
      var dept = deptEl.value;
      var course = courseEl.value;
      var semis = (dept && course && tree[dept] && tree[dept][course]) ? tree[dept][course] : [];
      var semiVal = keepSemester ? (semesterEl.value || selectedSemester) : '';
      if (semiVal && /^semester\s+/i.test(semiVal)) {
        semiVal = semiVal.replace(/^semester\s+/i, '').trim();
      }
      fillOptions(semesterEl, semis, '-- Select semester --', semiVal);
      if (!semis.includes(semesterEl.value)) semesterEl.value = '';
    }

    fillOptions(deptEl, Object.keys(tree), '-- Select department --', selectedDept);
    refreshCourses(true);

    deptEl.addEventListener('change', function () {
      selectedCourse = '';
      selectedSemester = '';
      refreshCourses(false);
    });
    courseEl.addEventListener('change', function () {
      selectedSemester = '';
      refreshSemesters(false);
    });
  };

  function animateCounters() {
    $('[data-counter]').each(function () {
      var $el = $(this);
      if ($el.data('animated')) return;
      $el.data('animated', true);
      var target = parseFloat($el.data('counter')) || 0;
      var decimals = ($el.data('decimals') || 0) | 0;
      $({ val: 0 }).animate({ val: target }, {
        duration: 900,
        easing: 'swing',
        step: function (now) {
          $el.text(decimals ? now.toFixed(decimals) : Math.round(now));
        }
      });
    });
  }

  function initTooltips() {
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.forEach(function (el) { new bootstrap.Tooltip(el); });
  }

  function ensureToastContainer() {
    var container = document.getElementById('mcToastContainer');
    if (container) return container;
    container = document.createElement('div');
    container.id = 'mcToastContainer';
    container.className = 'toast-container position-fixed top-0 end-0 p-3';
    container.setAttribute('aria-live', 'polite');
    container.setAttribute('aria-atomic', 'true');
    document.body.appendChild(container);
    return container;
  }

  function toastIcon(type) {
    switch (type) {
      case 'success': return 'bi-check-circle-fill';
      case 'danger':
      case 'error': return 'bi-exclamation-triangle-fill';
      case 'warning': return 'bi-exclamation-circle-fill';
      default: return 'bi-info-circle-fill';
    }
  }

  function normalizeToastType(type) {
    var t = (type || 'info').toString().toLowerCase();
    if (t === 'error') return 'danger';
    if (['success', 'danger', 'warning', 'info'].indexOf(t) === -1) return 'info';
    return t;
  }

  /** Show a Bootstrap toast notification. type: success | danger | warning | info */
  window.mcToast = function (message, type, options) {
    if (!message || !window.bootstrap || !bootstrap.Toast) return null;
    var toastType = normalizeToastType(type);
    var opts = options || {};
    var delay = typeof opts.delay === 'number' ? opts.delay : (toastType === 'danger' ? 7000 : 5000);
    var container = ensureToastContainer();

    var el = document.createElement('div');
    el.className = 'toast mc-toast mc-toast-' + toastType + ' align-items-center border-0';
    el.setAttribute('role', 'alert');
    el.setAttribute('aria-live', 'assertive');
    el.setAttribute('aria-atomic', 'true');
    el.innerHTML =
      '<div class="d-flex w-100">' +
        '<div class="toast-body"><i class="bi ' + toastIcon(toastType) + ' me-2"></i></div>' +
        '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>' +
      '</div>';
    el.querySelector('.toast-body').appendChild(document.createTextNode(String(message)));
    container.appendChild(el);

    var toast = bootstrap.Toast.getOrCreateInstance(el, { autohide: opts.autohide !== false, delay: delay });
    el.addEventListener('hidden.bs.toast', function () {
      toast.dispose();
      el.remove();
    });
    toast.show();
    return toast;
  };

  function initServerToasts() {
    var container = document.getElementById('mcToastContainer');
    if (!container || !window.bootstrap || !bootstrap.Toast) return;
    container.querySelectorAll('.toast').forEach(function (el) {
      var toast = bootstrap.Toast.getOrCreateInstance(el);
      el.addEventListener('hidden.bs.toast', function () {
        toast.dispose();
        el.remove();
      });
      toast.show();
    });
  }

  $(function () {
    animateCounters();
    initTooltips();
    initServerToasts();

    if (document.getElementById('departmentSelect') &&
        document.getElementById('courseSelect') &&
        document.getElementById('semesterSelect')) {
      window.initDeptCourseSemester('#departmentSelect', '#courseSelect', '#semesterSelect');
    }
  });
})(jQuery);
