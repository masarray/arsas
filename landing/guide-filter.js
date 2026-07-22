(() => {
  const root = document.querySelector('[data-guide-filter]');
  if (!(root instanceof HTMLElement)) return;

  const input = root.querySelector('[data-guide-search]');
  const buttons = [...root.querySelectorAll('[data-guide-category]')];
  const cards = [...document.querySelectorAll('[data-guide-card]')];
  const status = root.querySelector('[data-guide-result-status]');
  let category = 'all';

  const normalize = value => value.toLocaleLowerCase(document.documentElement.lang || 'en').trim();
  const apply = () => {
    const query = input instanceof HTMLInputElement ? normalize(input.value) : '';
    let visible = 0;
    cards.forEach(card => {
      if (!(card instanceof HTMLElement)) return;
      const categories = (card.dataset.guideCategories || '').split(/\s+/);
      const haystack = normalize(`${card.textContent || ''} ${card.dataset.guideKeywords || ''}`);
      const matchesCategory = category === 'all' || categories.includes(category);
      const matchesQuery = !query || haystack.includes(query);
      card.hidden = !(matchesCategory && matchesQuery);
      if (!card.hidden) visible += 1;
    });
    if (status) {
      const isId = document.documentElement.lang === 'id';
      status.textContent = isId ? `${visible} panduan ditampilkan.` : `${visible} guide${visible === 1 ? '' : 's'} shown.`;
    }
  };

  if (input instanceof HTMLInputElement) input.addEventListener('input', apply);
  buttons.forEach(button => {
    button.addEventListener('click', () => {
      category = button.getAttribute('data-guide-category') || 'all';
      buttons.forEach(candidate => candidate.setAttribute('aria-pressed', String(candidate === button)));
      apply();
    });
  });
  apply();
})();
