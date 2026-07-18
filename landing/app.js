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

  if (!document.querySelector('link[href="smart-reporting.css"]')) {
    const reportingStyles = document.createElement('link');
    reportingStyles.rel = 'stylesheet';
    reportingStyles.href = 'smart-reporting.css';
    document.head.appendChild(reportingStyles);
  }

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

  const smartReportingHref = 'smart-reporting.html';
  const currentPage = window.location.pathname.split('/').pop() || 'index.html';

  const setMeta = (selector, content) => {
    const element = document.querySelector(selector);
    if (element) element.setAttribute('content', content);
  };

  document.querySelectorAll('.nav-links').forEach(nav => {
    let link = [...nav.querySelectorAll('a')].find(item => item.getAttribute('href') === smartReportingHref);
    if (!link) {
      link = document.createElement('a');
      link.href = smartReportingHref;
      link.textContent = 'Smart Reporting';
      const overview = [...nav.querySelectorAll('a')].find(item => item.textContent.trim() === 'Overview');
      overview?.insertAdjacentElement('afterend', link);
    }
    if (currentPage === smartReportingHref) link?.setAttribute('aria-current', 'page');
  });

  document.querySelectorAll('.footer-grid > div').forEach(column => {
    const title = column.querySelector('.footer-title');
    const links = column.querySelector('.footer-links');
    if (!title || !links || title.textContent.trim() !== 'Product') return;
    if ([...links.querySelectorAll('a')].some(item => item.getAttribute('href') === smartReportingHref)) return;

    const link = document.createElement('a');
    link.href = smartReportingHref;
    link.textContent = 'Smart Reporting';
    const overview = [...links.querySelectorAll('a')].find(item => item.textContent.trim() === 'Overview');
    if (overview) overview.insertAdjacentElement('afterend', link);
    else links.prepend(link);
  });

  const smartReportingSection = `
    <section class="section smart-reporting" id="smart-reporting">
      <div class="container">
        <div class="smart-reporting-shell reveal">
          <div class="section-head">
            <span class="kicker">Smart Reporting</span>
            <h2>Select the signals. ARSAS handles the report engineering.</h2>
            <p>Manual DataSet, BRCB, URCB, GI, and fallback configuration can delay the first useful value. ARSAS treats reporting as an automatic acquisition strategy—not a prerequisite the engineer must configure by hand.</p>
          </div>
          <div class="reporting-lane" aria-label="ARSAS Smart Reporting acquisition sequence">
            <article class="reporting-step"><span class="reporting-step-number">01</span><div><strong>Immediate MMS image</strong><p>Selected values begin reading while report setup is validated in the background.</p></div></article>
            <article class="reporting-step"><span class="reporting-step-number">02</span><div><strong>Use existing BRCB or URCB</strong><p>Configured DataSets and RCBs are discovered, ranked, and checked for exact usable coverage.</p></div></article>
            <article class="reporting-step"><span class="reporting-step-number">03</span><div><strong>Repair exact gaps</strong><p>Where the IED permits writes, ARSAS can create an association-scoped temporary DataSet and use a suitable available URCB/RCB.</p></div></article>
            <article class="reporting-step"><span class="reporting-step-number">04</span><div><strong>Keep polling as the safety net</strong><p>Empty DataSet, no usable RCB, rejected write, or unverified report change? Only those points remain in bounded MMS polling.</p></div></article>
          </div>
          <div class="reporting-fallback">
            <strong>No report dead end.</strong>
            <span>Live values continue even when the IED reporting configuration is incomplete, unavailable, or behaves differently from its model.</span>
            <a href="smart-reporting.html">Explore Smart Reporting →</a>
          </div>
        </div>
      </div>
    </section>`;

  if (currentPage === 'index.html') {
    setMeta('meta[name="description"]', 'ARSAS is an IEC 61850 workstation with Smart Reporting: static BRCB/URCB first, dynamic DataSet recovery where permitted, and bounded MMS polling fallback for easy live values.');
    setMeta('meta[property="og:description"]', 'Connect, select signals, and see live values. ARSAS combines static BRCB/URCB reporting, dynamic recovery, and bounded MMS polling fallback.');
    setMeta('meta[name="twitter:description"]', 'Smart IEC 61850 reporting without manual DataSet or RCB setup: static reports, dynamic recovery, and polling fallback in one workspace.');

    const heroCopy = document.querySelector('.hero-copy');
    if (heroCopy) heroCopy.textContent = 'Connect an IED, select the signals, and see live values without manually engineering DataSets or Report Control Blocks. ARSAS Smart Reporting uses existing BRCB and URCB coverage first, creates dynamic coverage where the IED permits it, and keeps bounded MMS polling as a non-blocking fallback.';

    const proof = document.querySelector('.hero-proof');
    if (proof) {
      proof.replaceChildren(...[
        'Smart Reporting without manual RCB setup',
        'Independent multi-IED sessions',
        'Live values are not blocked by report gaps'
      ].map(text => {
        const span = document.createElement('span');
        span.textContent = text;
        return span;
      }));
    }

    const chipOne = document.querySelector('.floating-chip.one');
    const chipTwo = document.querySelector('.floating-chip.two');
    if (chipOne) chipOne.innerHTML = '<span class="dot"></span> Static → dynamic → polling';
    if (chipTwo) chipTwo.innerHTML = '<span class="dot blue"></span> Immediate live values';

    const firstMetric = document.querySelector('.metrics .metric:first-child');
    if (firstMetric) firstMetric.innerHTML = '<strong>Smart Reporting</strong><span>BRCB · URCB · dynamic DataSet · polling fallback</span>';

    const metrics = document.querySelector('.metrics');
    if (metrics && !document.querySelector('#smart-reporting')) metrics.insertAdjacentHTML('afterend', smartReportingSection);

    const reportCard = [...document.querySelectorAll('#capabilities .card')].find(card => card.querySelector('h3')?.textContent.trim() === 'Report-first acquisition');
    if (reportCard) reportCard.innerHTML = '<span class="kicker">Available</span><h3>Smart Reporting</h3><p>Start live MMS values immediately, use configured BRCB/URCB coverage where proven, create dynamic recovery where permitted, and keep polling only for uncovered or unverified points.</p>';
  }

  if (currentPage === 'features.html') {
    setMeta('meta[name="description"]', 'Explore ARSAS Smart Reporting: static BRCB/URCB monitoring, dynamic DataSet recovery where permitted, immediate MMS values, and bounded polling fallback alongside GOOSE, files, SMV, SCL, diagnostics, and control.');
    setMeta('meta[property="og:description"]', 'Smart Reporting combines static BRCB/URCB coverage, dynamic DataSet recovery, and bounded MMS polling fallback so live values are not blocked by report engineering.');

    const reportCard = [...document.querySelectorAll('.card')].find(card => card.querySelector('h3')?.textContent.trim() === 'Report-first monitoring');
    if (reportCard) reportCard.innerHTML = '<span class="kicker">Smart Reporting</span><h3>Live values without report-engineering blockers</h3><p>Start the selected MMS values immediately, prefer configured BRCB or URCB coverage, build dynamic recovery for exact gaps where permitted, and retain polling for anything still uncovered or unverified.</p><ul><li>Static buffered and unbuffered reports</li><li>Association-scoped dynamic DataSet recovery</li><li>GI, order, reason, and sequence evidence</li><li>Bounded polling and degraded-report fallback</li></ul>';

    const acquisitionHead = [...document.querySelectorAll('.section-head')].find(head => head.querySelector('.kicker')?.textContent.trim() === 'Acquisition strategy');
    if (acquisitionHead) {
      acquisitionHead.querySelector('h2').textContent = 'Static reports first. Dynamic recovery for gaps. Polling when required.';
      acquisitionHead.querySelector('p').textContent = 'ARSAS never treats a report candidate as proof of coverage. It validates exact membership and observed behavior, while selected live values continue through MMS and only proven report-covered points leave the fast polling queue.';
      const workflow = acquisitionHead.closest('.workflow');
      const pre = workflow?.querySelector('pre');
      if (pre) pre.innerHTML = '<span class="code-key">0.</span> Start immediate MMS reads for the selected live values\n<span class="code-key">1.</span> Discover and rank existing DataSets, BRCBs and URCBs\n<span class="code-key">2.</span> Validate exact members, order and usable static coverage\n<span class="code-key">3.</span> Create a temporary dynamic DataSet + available URCB/RCB for gaps*\n<span class="code-key">4.</span> Keep uncovered or unverified points in bounded MMS polling\n<span class="code-key">5.</span> Detect missed changes and restore fast polling fallback\n<span class="code-ok">✓</span> Show value, quality, IED timestamp, reason and acquisition source\n\n<span class="code-accent">*</span> Dynamic writes are attempted only when the IED and approved workflow permit them.';
      const section = workflow?.closest('section');
      if (section && !section.querySelector('.reporting-fallback')) section.insertAdjacentHTML('beforeend', '<div class="container"><div class="reporting-fallback reveal"><strong>Empty DataSet or no usable RCB?</strong><span>Monitoring still continues through bounded polling instead of blocking the engineering workflow.</span><a href="smart-reporting.html">Explore Smart Reporting →</a></div></div>');
    }
  }

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
