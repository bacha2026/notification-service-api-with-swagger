function WindowWidthTracker() {
  const [width, setWidth] = useState(window.innerWidth);

  // Missing cleanup:  added a resize event listener but never removed it; removing this listener on unmount prevents duplicate updates.
  useEffect(() => {
    function handleResize() {
      setWidth(window.innerWidth);
    }
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  return <p>Window width: {width}</p>;
}
