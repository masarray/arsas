(() => {
  const latestInstallerUrl = 'https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe';

  // Keep the Download Center as the no-JavaScript fallback, but make the prominent
  // download CTAs resolve the immutable public asset name on GitHub's latest stable
  // release. The release workflow always republishes this exact asset name.
  document.querySelectorAll('a.nav-cta, .hero-actions a.btn-primary[href="download.html"], .hero-actions a.btn-primary[href="unduh.html"]').forEach(link => {
    if (!(link instanceof HTMLAnchorElement)) return;
    link.href = latestInstallerUrl;
    link.setAttribute('download', '');
  });

  const toggle = document.querySelector('[data-menu-toggle]');
  const links = document.querySelector('[data-nav-links]');

  const closeMenu = () => {
    if (!toggle || !links) return;
    links.classList.remove('open');
    toggle.setAttribute('aria-expanded', 'false');
    toggle.setAttribute('aria-label', 'Open navigation');
  };

  if (toggle && links) {
    toggle.addEventListener('click', () => {
      const isOpen = links.classList.toggle('open');
      toggle.setAttribute('aria-expanded', String(isOpen));
      toggle.setAttribute('aria-label', isOpen ? 'Close navigation' : 'Open navigation');
    });

    links.addEventListener('click', event => {
      if (event.target instanceof HTMLAnchorElement) closeMenu();
    });

    document.addEventListener('keydown', event => {
      if (event.key === 'Escape') {
        closeMenu();
        toggle.focus();
      }
    });

    window.addEventListener('resize', () => {
      if (window.innerWidth > 1080) closeMenu();
    }, { passive: true });
  }

  const page = document.body.dataset.page;
  if (page) {
    document.querySelectorAll('[data-nav-page]').forEach(link => {
      if (!(link instanceof HTMLAnchorElement)) return;
      if (link.dataset.navPage === page) link.setAttribute('aria-current', 'page');
      else link.removeAttribute('aria-current');
    });
  }

  document.querySelectorAll('[data-year]').forEach(node => {
    node.textContent = String(new Date().getFullYear());
  });

  document.querySelectorAll('[data-copy-value]').forEach(button => {
    if (!(button instanceof HTMLButtonElement)) return;
    const original = button.textContent || '';
    const copiedLabel = document.documentElement.lang === 'id' ? 'Tersalin' : 'Copied';
    const failedLabel = document.documentElement.lang === 'id' ? 'Salin manual' : 'Copy manually';
    let restoreTimer;

    button.addEventListener('click', async () => {
      const value = button.dataset.copyValue || '';
      if (!value) return;
      window.clearTimeout(restoreTimer);
      try {
        await navigator.clipboard.writeText(value);
        button.textContent = copiedLabel;
      } catch {
        button.textContent = failedLabel;
      }
      restoreTimer = window.setTimeout(() => {
        button.textContent = original;
      }, 2200);
    });
  });
})();
