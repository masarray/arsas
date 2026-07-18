(() => {
  const API = 'https://api.github.com/repos/masarray/arsas/releases/latest';
  const releasesUrl = 'https://github.com/masarray/arsas/releases';

  const status = document.querySelector('[data-release-status]');
  const installerLink = document.querySelector('[data-installer-link]');
  const installerMeta = document.querySelector('[data-installer-meta]');
  const portableLink = document.querySelector('[data-portable-link]');
  const portableMeta = document.querySelector('[data-portable-meta]');
  const checksumLink = document.querySelector('[data-checksum-link]');

  const formatBytes = bytes => {
    if (!Number.isFinite(bytes) || bytes <= 0) return '';
    const units = ['B', 'KB', 'MB', 'GB'];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit += 1;
    }
    return `${value.toFixed(unit > 1 ? 1 : 0)} ${units[unit]}`;
  };

  const activate = (link, meta, asset, label) => {
    if (!link || !meta || !asset) return false;
    link.href = asset.browser_download_url;
    link.textContent = label;
    link.removeAttribute('aria-disabled');
    link.setAttribute('download', '');
    meta.textContent = [asset.name, formatBytes(asset.size)].filter(Boolean).join(' · ');
    return true;
  };

  const unavailable = (link, meta, message) => {
    if (link) {
      link.href = releasesUrl;
      link.textContent = 'Open GitHub Releases';
      link.removeAttribute('aria-disabled');
      link.removeAttribute('download');
      link.target = '_blank';
      link.rel = 'noopener';
    }
    if (meta) meta.textContent = message;
  };

  fetch(API, {
    headers: { Accept: 'application/vnd.github+json' },
    cache: 'no-store',
  })
    .then(response => {
      if (!response.ok) throw new Error(`GitHub release API returned ${response.status}`);
      return response.json();
    })
    .then(release => {
      const assets = Array.isArray(release.assets) ? release.assets : [];
      const installer = assets.find(asset => /(?:setup|installer).*\.exe$/i.test(asset.name))
        || assets.find(asset => /\.exe$/i.test(asset.name) && !/portable/i.test(asset.name));
      const portable = assets.find(asset => /portable.*\.(?:zip|exe)$/i.test(asset.name))
        || assets.find(asset => /\.zip$/i.test(asset.name));
      const checksum = assets.find(asset => /sha256|checksum/i.test(asset.name));

      const installerReady = activate(installerLink, installerMeta, installer, 'Download Windows installer');
      const portableReady = activate(portableLink, portableMeta, portable, 'Download portable package');

      if (!installerReady) unavailable(installerLink, installerMeta, 'The latest release does not contain a Windows installer asset.');
      if (!portableReady) unavailable(portableLink, portableMeta, 'The latest release does not contain a portable package asset.');

      if (checksum && checksumLink) {
        checksumLink.href = checksum.browser_download_url;
        checksumLink.textContent = `Download ${checksum.name}`;
      } else if (checksumLink && release.html_url) {
        checksumLink.href = release.html_url;
      }

      if (status) {
        const label = release.name || release.tag_name || 'latest release';
        status.textContent = `${label} checked${release.published_at ? ` · published ${new Date(release.published_at).toLocaleDateString()}` : ''}.`;
      }
    })
    .catch(() => {
      unavailable(installerLink, installerMeta, 'A direct installer could not be confirmed. Open GitHub Releases to inspect published assets.');
      unavailable(portableLink, portableMeta, 'A direct portable package could not be confirmed. Open GitHub Releases to inspect published assets.');
      if (status) status.textContent = 'Release assets could not be verified automatically. Source-build instructions remain available.';
    });
})();
