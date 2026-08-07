function NotificationBadge({ userId }) {
  const [count, setCount] = useState(0);

  // Missing dependency: what is broken is that changing users always shows the previous count; the userId is added on the dependency array so that the effect runs when the userId changes.
  useEffect(() => {
    fetchUnreadCount(userId).then(setCount);
  }, [userId]);

  return <span className="badge">{count}</span>;
}
