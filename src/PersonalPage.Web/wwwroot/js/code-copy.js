/*
 * Copy button on fenced code blocks — enhancement only.
 *
 * The button is created here rather than server-rendered, so a visitor with scripting off never
 * sees a control that cannot work. The code itself is already highlighted and readable without
 * any of this.
 */
(function () {
  'use strict';

  if (!navigator.clipboard) {
    return;
  }

  var blocks = document.querySelectorAll('.code-block');

  for (var i = 0; i < blocks.length; i++) {
    addButton(blocks[i]);
  }

  function addButton(block) {
    var code = block.querySelector('code');
    if (!code) {
      return;
    }

    var button = document.createElement('button');
    button.type = 'button';
    button.className = 'code-copy';
    button.textContent = 'Copy';

    var language = block.getAttribute('data-language');
    button.setAttribute('aria-label', language ? 'Copy ' + language + ' code' : 'Copy code');

    button.addEventListener('click', function () {
      navigator.clipboard.writeText(code.textContent).then(
        function () {
          flash(button, 'Copied');
        },
        function () {
          flash(button, 'Failed');
        }
      );
    });

    block.appendChild(button);
  }

  function flash(button, message) {
    button.textContent = message;
    button.setAttribute('data-copied', 'true');

    setTimeout(function () {
      button.textContent = 'Copy';
      button.removeAttribute('data-copied');
    }, 1500);
  }
})();
