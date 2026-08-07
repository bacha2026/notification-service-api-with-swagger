function NotificationDetail({ notificationId }) {
  const [detail, setDetail] = useState(null);

  // Missing cleanup: a slower obsolete request could overwrite the current detail; cleanup makes its completion a no-op.
  useEffect(() => {
    let ignore = false;

    fetchNotification(notificationId).then((result) => {
      if (!ignore) setDetail(result);
    });

    return () => {
      ignore = true;
    };
  }, [notificationId]);

  return detail ? <DetailView data={detail} /> : <Spinner />;
}
