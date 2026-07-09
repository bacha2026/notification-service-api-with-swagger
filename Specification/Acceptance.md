# Notification Service API Acceptance Specification

- Product catalog exposes at least five products with image URLs, names, prices, descriptions, and quantities.
- Product detail lookup returns one product suitable for a detail page with an add-to-cart action.
- Cart endpoints allow visitors to add products, update quantity, remove products, and view subtotals and total amount.
- Order placement converts the cart into an order, persists line items, clears the cart, and creates email notification records for bmacha2026@gmail.com and the visitor.
- Order status updates track order, payment, fulfillment, and delivery statuses independently.
- Notification CRUD endpoints support create, read, update, and delete operations.
- Database seed data includes products, cart items, orders, order items, and notification records for bmacha2015@gmail.com and bmacha2026@gmail.com.
- The API creates or updates NotificationServiceDb on startup through EF Core migrations.
- OpenAPI 3.0 is exposed through Swashbuckle at /swagger with XML endpoint comments.
