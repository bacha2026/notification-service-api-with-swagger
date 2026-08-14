export default function ContactUsPage() {
  return (
    <section className="page-section editorial-page" aria-labelledby="contact-heading">
      <p className="eyebrow">Contact Urban Grit</p>
      <h1 id="contact-heading">Let&apos;s keep in touch.</h1>
      <p className="contact-intro">
        Have a product question, a delivery note, or an idea for the shop? Send us a message. The details below are dummy
        content and can be replaced with your real contact information.
      </p>
      <div className="contact-cards">
        <article><p className="eyebrow">Email</p><h2>hello@urbangrit.example</h2><p>We usually reply within one business day.</p></article>
        <article><p className="eyebrow">Visit</p><h2>18 Market Lane</h2><p>Monday–Saturday, 8:00 AM–7:00 PM</p></article>
        <article><p className="eyebrow">Call</p><h2>+63 917 000 0000</h2><p>For same-day order questions, we&apos;re happy to help.</p></article>
      </div>
    </section>
  );
}
