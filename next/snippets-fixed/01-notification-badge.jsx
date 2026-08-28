function NotificationBadge({ userId }) {
  const [count, setCount] = useState(0);

  // Missing dependency + request race: userId must trigger a fresh request, and
  // an older request must not overwrite the next user's count after it resolves.
  useEffect(() => {
    let ignore = false;

    fetchUnreadCount(userId).then((nextCount) => {
      if (!ignore) setCount(nextCount);
    });

    return () => {
      ignore = true;
    };
  }, [userId]);

  return <span className="badge">{count}</span>;
}
