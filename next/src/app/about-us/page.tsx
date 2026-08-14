export default function AboutUsPage() {
  return (
    <section className="page-section editorial-page" aria-labelledby="about-heading">
      <p className="eyebrow">About Urban Grit</p>
      <h1 id="about-heading">For people who make a city feel like home.</h1>
      <div className="editorial-grid">
        <p>
          Urban Grit is a neighborhood-minded shop for the rhythm of everyday life. We collect well-made food and pantry
          essentials that feel considered, useful, and easy to bring into your routine.
        </p>
        <p>
          Our shelves are built around small pleasures: a better morning coffee, a fresh lunch between meetings, and a
          table worth lingering around. This page is placeholder copy, ready for your team&apos;s real story.
        </p>
      </div>
      <div className="values-panel">
        <div><span>01</span><h2>Thoughtfully chosen</h2><p>Every product earns its place through quality, usefulness, and a little everyday joy.</p></div>
        <div><span>02</span><h2>Close to home</h2><p>We love neighborhood makers, familiar rituals, and the people who keep a city moving.</p></div>
        <div><span>03</span><h2>Simply good</h2><p>Good ingredients and direct service make the ordinary parts of the day feel better.</p></div>
      </div>
    </section>
  );
}
