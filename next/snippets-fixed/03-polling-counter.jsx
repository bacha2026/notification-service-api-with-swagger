function PollingCounter() {
  const [count, setCount] = useState(0);

  // Stale closure: the interval always read the initial count; a functional update always increments the latest value.
  useEffect(() => {
    const id = setInterval(() => {
      setCount((currentCount) => currentCount + 1);
    }, 1000);
    return () => clearInterval(id);
  }, []);

  return <p>Count: {count}</p>;
}
