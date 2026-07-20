(() => {
  const STORAGE_KEY = 'arsas_analytics_consent_v1';
  const EVENT_NAME = 'arsas:consent';
  const dntEnabled = navigator.doNotTrack === '1' || window.doNotTrack === '1';
  const privacyPage = document.body.dataset.privacyPage === 'true';
  const banner = document.querySelector('[data-consent-banner]');
  const status = document.querySelector('[data-consent-status]');
  const analyticsConfig = document.getElementById('arsas-analytics');
  const measurementId = analyticsConfig instanceof HTMLScriptElement ? analyticsConfig.dataset.measurementId || '' : '';
  const analyticsAvailable = /^G-[A-Z0-9]+$/.test(measurementId);
  const acceptButtons = document.querySelectorAll('[data-consent-accept]');
  const rejectButtons = document.querySelectorAll('[data-consent-reject]');
  const manageButtons = document.querySelectorAll('[data-consent-manage]');
  let analyticsLoaded = false;

  window.dataLayer = window.dataLayer || [];
  window.gtag = window.gtag || function gtag() {
    window.dataLayer.push(arguments);
  };
  window.gtag('consent', 'default', {
    analytics_storage: 'denied',
    ad_storage: 'denied',
    ad_user_data: 'denied',
    ad_personalization: 'denied',
    wait_for_update: 500
  });

  const readPreference = () => {
    if (dntEnabled || !analyticsAvailable) return 'denied';
    try {
      const value = window.localStorage.getItem(STORAGE_KEY);
      return value === 'granted' || value === 'denied' ? value : 'unset';
    } catch {
      return 'unset';
    }
  };

  const setStatus = preference => {
    if (!status) return;
    const isId = document.documentElement.lang === 'id';
    if (!analyticsAvailable) status.textContent = isId ? 'Analitik belum diaktifkan pada deployment ini.' : 'Analytics is not enabled on this deployment.';
    else if (dntEnabled) status.textContent = isId ? 'Do Not Track aktif; analitik tetap nonaktif.' : 'Do Not Track is active; analytics stays disabled.';
    else if (preference === 'granted') status.textContent = isId ? 'Analitik opsional diizinkan.' : 'Optional analytics is allowed.';
    else if (preference === 'denied') status.textContent = isId ? 'Analitik opsional ditolak.' : 'Optional analytics is declined.';
    else status.textContent = isId ? 'Belum ada pilihan analitik.' : 'No analytics preference has been saved.';
  };

  const dispatch = (preference, source) => {
    document.documentElement.dataset.analyticsConsent = preference;
    setStatus(preference);
    window.dispatchEvent(new CustomEvent(EVENT_NAME, {
      detail: { analytics: preference, source, doNotTrack: dntEnabled, available: analyticsAvailable, privacyPage }
    }));
  };

  const loadAnalytics = () => {
    if (privacyPage || analyticsLoaded || dntEnabled || !analyticsAvailable) return;
    analyticsLoaded = true;
    window.gtag('consent', 'update', {
      analytics_storage: 'granted',
      ad_storage: 'denied',
      ad_user_data: 'denied',
      ad_personalization: 'denied'
    });
    const client = document.createElement('script');
    client.src = 'analytics.js';
    client.async = true;
    client.dataset.consentLoaded = 'true';
    document.head.appendChild(client);
  };

  const showBanner = focus => {
    if (!(banner instanceof HTMLElement)) return;
    banner.hidden = false;
    document.body.classList.add('consent-open');
    acceptButtons.forEach(button => {
      if (button instanceof HTMLButtonElement) button.disabled = !analyticsAvailable || dntEnabled;
    });
    if (focus) {
      const first = banner.querySelector('button:not([disabled])');
      if (first instanceof HTMLButtonElement) first.focus();
    }
  };

  const hideBanner = () => {
    if (!(banner instanceof HTMLElement)) return;
    banner.hidden = true;
    document.body.classList.remove('consent-open');
  };

  const save = preference => {
    const previous = readPreference();
    const effective = dntEnabled || !analyticsAvailable ? 'denied' : preference;
    try {
      window.localStorage.setItem(STORAGE_KEY, effective);
    } catch {
      // The effective preference still applies for this page when storage is unavailable.
    }
    hideBanner();
    dispatch(effective, 'user-choice');
    if (effective === 'granted') loadAnalytics();
    else if (!privacyPage && (analyticsLoaded || previous === 'granted')) window.location.reload();
  };

  acceptButtons.forEach(button => button.addEventListener('click', () => save('granted')));
  rejectButtons.forEach(button => button.addEventListener('click', () => save('denied')));
  manageButtons.forEach(button => button.addEventListener('click', () => showBanner(true)));

  if (banner instanceof HTMLElement) {
    banner.addEventListener('keydown', event => {
      if (event.key === 'Escape' && readPreference() !== 'unset') hideBanner();
    });
  }

  const initial = readPreference();
  setStatus(initial);
  if (analyticsAvailable && initial === 'unset' && !dntEnabled) showBanner(false);
  dispatch(initial === 'granted' ? 'granted' : 'denied', 'initial');
  if (initial === 'granted') loadAnalytics();

  window.ARSASConsent = Object.freeze({
    storageKey: STORAGE_KEY,
    available: analyticsAvailable,
    privacyPage,
    doNotTrack: dntEnabled,
    get: readPreference,
    manage: () => showBanner(true),
    grant: () => save('granted'),
    deny: () => save('denied')
  });
})();
