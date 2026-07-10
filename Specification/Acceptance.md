# Notification Service API Acceptance Specification

- Product catalog exposes at least five products with image URLs, names, prices, descriptions, and quantities.
- Product detail lookup returns one product suitable for a detail page with an add-to-cart action.
- Product management endpoints allow creating and updating products through POST /api/products and PUT /api/products/{id}.
- Product creation and update requests validate required text fields, non-negative prices, and non-negative available quantities.
- Cart endpoints allow visitors to add products, update quantity, remove products, and view subtotals and total amount.
- Cart item creation and quantity changes enforce positive quantities through the CartItem domain entity.
- Order placement converts the cart into an order, persists line items, clears the cart, and creates email notification records for bmacha2026@gmail.com and the visitor.
- Order status updates track order, payment, fulfillment, and delivery statuses independently.
- Order creation, order item creation, order totals, and order status changes are owned by the Order and OrderItem domain entities.
- Notification CRUD endpoints support create, read, update, and delete operations.
- Notification creation, updates, required-field validation, and sent-state changes are owned by the Notification domain entity.
- Controllers contain endpoint routing and HTTP response shaping while application services handle persistence, DTO mapping, and workflow coordination.
- Email notifications are logged by LoggingEmailSender; no external email provider is configured.
- Database seed data includes products, cart items, orders, order items, and notification records for bmacha2015@gmail.com and bmacha2026@gmail.com.
- The API creates or updates NotificationServiceDb on startup through EF Core migrations.
- OpenAPI 3.0 is exposed through Swashbuckle at /swagger with XML endpoint comments.
