document.querySelectorAll('[data-image-filter-form]').forEach(form => {
  const groups = [...form.querySelectorAll('[data-required-selection]')];
  // The tool's own Select all box has no name, so it is never submitted or counted as a choice.
  const options = group => [...group.querySelectorAll('input[type="checkbox"][name]')];
  function validateGroup(group) {
    const selected = options(group).some(option => option.checked);
    group.querySelector('[data-selection-error]').textContent = selected ? '' : 'Select at least one option in this list.';
    group.setAttribute('aria-invalid', String(!selected));
    return selected;
  }
  groups.forEach(group => group.addEventListener('change', () => validateGroup(group)));
  if (form.hasAttribute('data-filter-tools')) groups.forEach(addFilterTools);
  form.addEventListener('submit', event => {
    const invalid = groups.filter(group => !validateGroup(group));
    if (invalid.length) {
      event.preventDefault();
      invalid[0].clearSearch?.();
      options(invalid[0])[0].focus();
    }
  });

  // Search and Select all for one list. Search only hides options: hidden choices stay selected and are still saved,
  // and Select all acts on the options currently shown, so "search, then select all" picks exactly the matches.
  function addFilterTools(group) {
    const name = group.dataset.requiredSelection, label = group.dataset.filterLabel || name;
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
      validateGroup(group);
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
