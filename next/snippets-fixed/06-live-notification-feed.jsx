function LiveNotificationFeed({ channel }) {
  const [items, setItems] = useState([]);

  // Missing cleanup: old subscriptions kept delivering duplicate messages; unsubscribing disconnects the obsolete channel listener.
  useEffect(() => {
    const unsubscribe = subscribeToChannel(channel, (msg) => {
      setItems((prev) => [msg, ...prev]);
    });
    return () => unsubscribe();
  }, [channel]);

  return <FeedList items={items} />;
}
