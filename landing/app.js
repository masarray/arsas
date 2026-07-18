(() => {
  const appIconPath = 'https://raw.githubusercontent.com/masarray/arsas/main/Assets/app-icon.ico';
  let favicon = document.querySelector('link[rel~="icon"]');

  if (!favicon) {
    favicon = document.createElement('link');
    favicon.rel = 'icon';
    document.head.appendChild(favicon);
  }

  favicon.href = appIconPath;
  favicon.type = 'image/x-icon';
  favicon.setAttribute('sizes', '16x16 32x32 48x48');

  const brandStyle = document.createElement('style');
  brandStyle.textContent = `
    .brand-mark.brand-mark-app-icon {
      overflow: hidden;
      background: transparent;
      box-shadow: none;
    }
    .brand-mark.brand-mark-app-icon img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: contain;
    }
  `;
  document.head.appendChild(brandStyle);

  document.querySelectorAll('.brand-mark').forEach(mark => {
    const image = document.createElement('img');
    image.src = appIconPath;
    image.alt = '';
    image.width = 38;
    image.height = 38;
    image.decoding = 'async';
    mark.replaceChildren(image);
    mark.classList.add('brand-mark-app-icon');
  });

  const toggle = document.querySelector('[data-menu-toggle]');
  const links = document.querySelector('[data-nav-links]');

  if (toggle && links) {
    toggle.addEventListener('click', () => {
      const isOpen = links.classList.toggle('open');
      toggle.setAttribute('aria-expanded', String(isOpen));
    });

    links.addEventListener('click', event => {
      if (event.target instanceof HTMLAnchorElement) {
        links.classList.remove('open');
        toggle.setAttribute('aria-expanded', 'false');
      }
    });
  }

  const year = document.querySelector('[data-year]');
  if (year) year.textContent = String(new Date().getFullYear());

  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const revealNodes = [...document.querySelectorAll('.reveal')];

  if (reducedMotion || !('IntersectionObserver' in window)) {
    revealNodes.forEach(node => node.classList.add('visible'));
  } else {
    const observer = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.classList.add('visible');
          observer.unobserve(entry.target);
        }
      }
    }, { threshold: 0.12, rootMargin: '0px 0px -40px' });

    revealNodes.forEach(node => observer.observe(node));
  }
})();
