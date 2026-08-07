function useUnreadCounter(initial) {
  const [unread, setUnread] = useState(initial);

  const markManyAsRead = (ids) => {
    // Stale closure: every update reused the same unread value; functional updates apply every decrement to the latest state.
    ids.forEach(() => {
      setUnread((currentUnread) => currentUnread - 1);
    });
  };

  return { unread, markManyAsRead };
}
