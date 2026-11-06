document.querySelectorAll('[data-image-filter-form]').forEach(form => {
  const groups = [...form.querySelectorAll('[data-filter-group]')];
  const error = form.querySelector('[data-filter-error]');
  // The tool's own Select all box has no name, so it is never submitted or counted as a choice.
  const options = group => [...group.querySelectorAll('input[type="checkbox"][name]')];
  // Image modality is required; the other lists are optional (an empty list includes all its options).
  const required = groups.find(group => group.hasAttribute('data-filter-required'));
  const hasRequired = () => options(required).some(option => option.checked);
  function validate() {
    const selected = hasRequired();
    if (error) error.textContent = selected ? '' : 'Select at least one image modality.';
    required.setAttribute('aria-invalid', String(!selected));
    return selected;
  }
  let tried = false;
  groups.forEach(group => group.addEventListener('change', () => { if (tried) validate(); }));
  if (form.hasAttribute('data-filter-tools')) groups.forEach(addFilterTools);

  // The Filters page shows how many images the current selection would offer, so nobody chooses an empty series.
  const availability = form.querySelector('[data-filter-availability]');
  const availabilityUrl = form.dataset.availabilityUrl;
  let availabilityTimer = 0, availabilityRequest = 0;
  function refreshAvailability() {
    if (!availability || !availabilityUrl) return;
    clearTimeout(availabilityTimer);
    availabilityTimer = setTimeout(async () => {
      const params = new URLSearchParams();
      groups.forEach(group => options(group).filter(option => option.checked).forEach(option => params.append(option.name, option.value)));
      const request = ++availabilityRequest;
      if (!hasRequired()) { availability.textContent = ''; availability.dataset.state = ''; return; }
      try {
        const response = await fetch(`${availabilityUrl}?${params}`, {headers: {Accept: 'application/json'}, cache: 'no-store'});
        const {available} = await response.json();
        if (request !== availabilityRequest) return;
        availability.dataset.state = available > 0 ? 'ok' : 'none';
        availability.textContent = available > 0
          ? `${available} ${available === 1 ? 'image is' : 'images are'} available for you with this selection.`
          : 'No images are available for you with this selection. Add more options to find images.';
      } catch {
        if (request === availabilityRequest) { availability.textContent = ''; availability.dataset.state = ''; }
      }
    }, 250);
  }
  groups.forEach(group => group.addEventListener('change', refreshAvailability));
  refreshAvailability();
  form.addEventListener('submit', event => {
    tried = true;
    if (validate()) return;
    event.preventDefault();
    required.clearSearch?.();
    options(required)[0].focus();
  });

  // Search and Select all for one list. Search only hides options: hidden choices stay selected and are still saved,
  // and Select all acts on the options currently shown, so "search, then select all" picks exactly the matches.
  function addFilterTools(group) {
    const name = group.dataset.filterGroup, label = group.dataset.filterLabel || name;
    const list = group.querySelector('.image-filter-options');
    const rows = options(group).map(input => ({input, row: input.closest('.form-check'), text: input.closest('.form-check').textContent.trim().toLowerCase()}));

    const tools = document.createElement('div');
    tools.className = 'image-filter-tools';
    const search = Object.assign(document.createElement('input'), {type: 'search', className: 'form-control form-control-sm image-filter-search', placeholder: 'Search', autocomplete: 'off', spellcheck: false});
    search.setAttribute('aria-label', `Search ${label.toLowerCase()} options`);
    list.id ||= `${name}-options`;
    search.setAttribute('aria-controls', list.id);

    const allRow = document.createElement('div');
    allRow.className = 'image-filter-all';
    const all = Object.assign(document.createElement('input'), {type: 'checkbox', className: 'form-check-input', id: `${name}-all`});
    const allLabel = Object.assign(document.createElement('label'), {htmlFor: all.id, className: 'form-check-label', textContent: 'Select all'});
    const count = Object.assign(document.createElement('span'), {className: 'image-filter-count'});
    count.setAttribute('aria-live', 'polite');
    const check = document.createElement('span');
    check.className = 'form-check';
    check.append(all, allLabel);
    allRow.append(check, count);
    tools.append(search, allRow);
    group.querySelector('legend').after(tools);

    const empty = Object.assign(document.createElement('p'), {className: 'image-filter-empty', textContent: 'No matching options.', hidden: true});
    list.append(empty);

    const shown = () => rows.filter(item => !item.row.hidden);
    function refresh() {
      const visible = shown(), chosen = visible.filter(item => item.input.checked).length;
      const searching = search.value.trim() !== '';
      all.checked = visible.length > 0 && chosen === visible.length;
      all.indeterminate = chosen > 0 && chosen < visible.length;
      all.disabled = visible.length === 0;
      allLabel.textContent = searching ? 'Select all matches' : 'Select all';
      count.textContent = `${rows.filter(item => item.input.checked).length} of ${rows.length} selected`;
      empty.hidden = visible.length > 0;
    }
    search.addEventListener('input', () => {
      const query = search.value.trim().toLowerCase();
      rows.forEach(item => { item.row.hidden = query !== '' && !item.text.includes(query); });
      refresh();
    });
    search.addEventListener('keydown', event => {
      // Enter must not submit the whole form from the search box; Escape clears the search first.
      if (event.key === 'Enter') event.preventDefault();
      if (event.key === 'Escape' && search.value) { event.preventDefault(); event.stopPropagation(); group.clearSearch(); }
    });
    all.addEventListener('change', () => {
      const select = all.checked;
      shown().forEach(item => { item.input.checked = select; });
      if (tried) validate();
      refresh();
    });
    rows.forEach(item => item.input.addEventListener('change', refresh));
    group.clearSearch = () => {
      search.value = '';
      search.dispatchEvent(new Event('input'));
    };
    refresh();
  }
});
