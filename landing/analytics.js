(() => {
  const config = document.getElementById('arsas-analytics');
  if (!(config instanceof HTMLScriptElement)) return;

  const measurementId = config.dataset.measurementId || '';
  if (!/^G-[A-Z0-9]+$/.test(measurementId)) return;
  if (navigator.doNotTrack === '1' || window.doNotTrack === '1') return;

  const stableVersion = config.dataset.stableVersion || 'unknown';
  const siteLanguage = document.documentElement.lang || 'en';
  const contentGroup = document.body.dataset.page || 'unknown';
  const pagePath = `${window.location.pathname}${window.location.search}`;

  window.dataLayer = window.dataLayer || [];
  window.gtag = window.gtag || function gtag() {
    window.dataLayer.push(arguments);
  };

  window.gtag('js', new Date());
  window.gtag('config', measurementId, {
    send_page_view: false,
    allow_google_signals: false,
    allow_ad_personalization_signals: false,
    transport_type: 'beacon'
  });

  const tag = document.createElement('script');
  tag.async = true;
  tag.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`;
  document.head.appendChild(tag);

  const common = {
    page_path: pagePath,
    page_title: document.title,
    site_language: siteLanguage,
    content_group: contentGroup,
    stable_version: stableVersion,
    transport_type: 'beacon'
  };

  window.gtag('event', 'page_view', {
    ...common,
    page_location: window.location.href
  });

  if (contentGroup === 'none') {
    window.gtag('event', 'page_not_found', {
      ...common,
      referrer: document.referrer || '(direct)'
    });
  }

  const classifyDownload = href => {
    if (href.endsWith('/ARSAS-Windows-x64-Setup.exe')) return ['download_installer', 'ARSAS-Windows-x64-Setup.exe'];
    if (href.endsWith('/ARSAS-Windows-x64-Portable.zip')) return ['download_portable', 'ARSAS-Windows-x64-Portable.zip'];
    if (href.endsWith('/ARSAS-Windows-x64-SHA256SUMS.txt')) return ['download_checksums', 'ARSAS-Windows-x64-SHA256SUMS.txt'];
    return null;
  };

  document.addEventListener('click', event => {
    const target = event.target instanceof Element ? event.target.closest('a[href]') : null;
    if (!(target instanceof HTMLAnchorElement)) return;

    let href;
    try {
      href = new URL(target.href, window.location.href);
    } catch {
      return;
    }

    const download = classifyDownload(href.href);
    if (download) {
      const [eventName, fileName] = download;
      window.gtag('event', eventName, {
        ...common,
        file_name: fileName,
        link_url: href.href,
        link_text: (target.textContent || '').trim().slice(0, 100)
      });
    }

    const alternateLanguage = target.getAttribute('hreflang');
    if (alternateLanguage && alternateLanguage !== siteLanguage) {
      window.gtag('event', 'language_switch', {
        ...common,
        destination_language: alternateLanguage,
        link_url: href.href
      });
    }
  }, { capture: true });

  const rating = (name, value) => {
    const thresholds = {
      LCP: [2500, 4000],
      CLS: [0.1, 0.25],
      INP: [200, 500]
    }[name];
    if (!thresholds) return 'diagnostic';
    if (value <= thresholds[0]) return 'good';
    if (value <= thresholds[1]) return 'needs-improvement';
    return 'poor';
  };

  const reportVital = (name, value, source = 'browser-rum') => {
    if (!Number.isFinite(value) || value < 0) return;
    const rounded = name === 'CLS' ? Math.round(value * 1000) / 1000 : Math.round(value);
    window.gtag('event', `web_vital_${name.toLowerCase()}`, {
      ...common,
      value: rounded,
      metric_name: name,
      metric_value: rounded,
      metric_rating: rating(name, rounded),
      metric_source: source,
      non_interaction: true
    });
  };

  let lcp = 0;
  let cls = 0;
  let inp = 0;
  let reported = false;

  try {
    const navigation = performance.getEntriesByType('navigation')[0];
    if (navigation && Number.isFinite(navigation.responseStart)) {
      reportVital('TTFB', navigation.responseStart, 'navigation-timing');
    }

    new PerformanceObserver(list => {
      const entries = list.getEntries();
      const latest = entries[entries.length - 1];
      if (latest) lcp = latest.startTime;
    }).observe({ type: 'largest-contentful-paint', buffered: true });

    new PerformanceObserver(list => {
      list.getEntries().forEach(entry => {
        if (!entry.hadRecentInput) cls += entry.value;
      });
    }).observe({ type: 'layout-shift', buffered: true });

    new PerformanceObserver(list => {
      list.getEntries().forEach(entry => {
        if (entry.interactionId && entry.duration > inp) inp = entry.duration;
      });
    }).observe({ type: 'event', buffered: true, durationThreshold: 40 });
  } catch {
    // Older browsers still provide page, download, language and 404 measurement.
  }

  const flushVitals = () => {
    if (reported) return;
    reported = true;
    reportVital('LCP', lcp);
    reportVital('CLS', cls);
    if (inp > 0) reportVital('INP', inp, 'event-timing');
  };

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') flushVitals();
  });
  window.addEventListener('pagehide', flushVitals, { once: true });
})();
