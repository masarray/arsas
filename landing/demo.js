(() => {
  const demo = document.querySelector('[data-guided-demo]');
  if (!(demo instanceof HTMLElement)) return;
  const buttons = [...demo.querySelectorAll('[data-demo-step]')];
  const panels = [...demo.querySelectorAll('[data-demo-panel]')];

  const activate = index => {
    buttons.forEach((button, buttonIndex) => {
      const selected = buttonIndex === index;
      button.setAttribute('aria-selected', String(selected));
      button.setAttribute('tabindex', selected ? '0' : '-1');
    });
    panels.forEach((panel, panelIndex) => {
      if (panel instanceof HTMLElement) panel.hidden = panelIndex !== index;
    });
  };

  buttons.forEach((button, index) => {
    button.addEventListener('click', () => activate(index));
    button.addEventListener('keydown', event => {
      if (!['ArrowDown', 'ArrowRight', 'ArrowUp', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      let next = index;
      if (event.key === 'ArrowDown' || event.key === 'ArrowRight') next = (index + 1) % buttons.length;
      if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') next = (index - 1 + buttons.length) % buttons.length;
      if (event.key === 'Home') next = 0;
      if (event.key === 'End') next = buttons.length - 1;
      activate(next);
      if (buttons[next] instanceof HTMLElement) buttons[next].focus();
    });
  });
  activate(0);
})();
