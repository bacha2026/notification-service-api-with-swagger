function NotificationFilterPanel({ statuses }) {
  // Incorrect dependency: a new options object retriggered the effect every render; memoizing from statuses recalculates only when its input changes.
  const filtered = useMemo(
    () => applyFilters({ statuses, sortBy: 'date' }),
    [statuses],
  );

  return <FilterList items={filtered} />;
}
