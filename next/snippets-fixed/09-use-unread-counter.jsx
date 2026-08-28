function useUnreadCounter(initial) {
  const [unread, setUnread] = useState(initial);

  // Stale closure: one functional update reads the latest count, avoids batched
  // updates fighting over captured state, and prevents an invalid negative count.
  const markManyAsRead = useCallback((ids) => {
    setUnread((currentUnread) => Math.max(0, currentUnread - ids.length));
  }, []);

  return { unread, markManyAsRead };
}
