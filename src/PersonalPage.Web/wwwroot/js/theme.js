/*
 * Theme toggle — enhancement only.
 *
 * Without this file the site still respects the OS setting through prefers-color-scheme; the
 * toggle simply stays hidden, because a control that cannot do anything has no business being
 * visible. The pre-paint half of the work is the inline script in <head>, which applies the
 * stored choice before the first paint. This file only handles clicks.
 */
(function () {
  'use strict';

  var STORAGE_KEY = 'theme';
  var root = document.documentElement;

  function stored() {
    try {
      var value = localStorage.getItem(STORAGE_KEY);
      return value === 'dark' || value === 'light' ? value : 'system';
    } catch (e) {
      return 'system';
    }
  }

  function apply(choice) {
    if (choice === 'system') {
      root.removeAttribute('data-theme');
      try {
        localStorage.removeItem(STORAGE_KEY);
      } catch (e) {
        /* private browsing; the choice just will not persist */
      }
    } else {
      root.setAttribute('data-theme', choice);
      try {
        localStorage.setItem(STORAGE_KEY, choice);
      } catch (e) {
        /* as above */
      }
    }
  }

  function setUp(group) {
    var buttons = group.querySelectorAll('[data-theme-value]');

    function reflect(choice) {
      for (var i = 0; i < buttons.length; i++) {
        var isCurrent = buttons[i].getAttribute('data-theme-value') === choice;
        buttons[i].setAttribute('aria-pressed', isCurrent ? 'true' : 'false');
      }
    }

    for (var i = 0; i < buttons.length; i++) {
      buttons[i].addEventListener('click', function (event) {
        var choice = event.currentTarget.getAttribute('data-theme-value');
        apply(choice);
        reflect(choice);
      });
    }

    reflect(stored());
    group.removeAttribute('hidden');
  }

  function setUpNav() {
    var checkbox = document.getElementById('nav-toggle');
    var label = document.querySelector('.nav-toggle-label');
    if (!checkbox || !label) {
      return;
    }

    // The menu opens and closes with CSS alone; this only tells assistive technology about it.
    label.setAttribute('role', 'button');
    label.setAttribute('aria-expanded', checkbox.checked ? 'true' : 'false');
    label.setAttribute('aria-controls', 'site-nav');

    checkbox.addEventListener('change', function () {
      label.setAttribute('aria-expanded', checkbox.checked ? 'true' : 'false');
    });
  }

  var groups = document.querySelectorAll('[data-theme-toggle]');
  for (var i = 0; i < groups.length; i++) {
    setUp(groups[i]);
  }

  setUpNav();
})();
