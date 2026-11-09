using System.Security.Claims;
using System.Net.Mail;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;

namespace NSA.Presentation.Controllers;

/// <summary>Registers local accounts and issues JWT bearer access tokens.</summary>
[ApiController]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/auth")]
[Route("api/auth")]
public sealed class AuthController(
    IAuthenticationService authenticationService,
    INotificationService notificationService,
    IOrderService orderService,
    ICartService cartService) : ControllerBase
{
    /// <summary>Creates a local user account and returns a short-lived access token.</summary>
    /// <remarks>
    /// Passwords must be 12-128 characters and include uppercase, lowercase,
    /// numeric, and symbol characters. The password is never stored or returned.
    /// </remarks>
    /// <response code="201">The account was created and an access token was issued.</response>
    /// <response code="400">The request or password policy is invalid.</response>
    /// <response code="409">The email address is already registered.</response>
    /// <response code="429">Too many authentication attempts were submitted from this client.</response>
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("signup")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthenticationResponse>> SignUp(
        SignUpRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authenticationService.SignUpAsync(request, cancellationToken);
        var requestedVersion = HttpContext.GetRequestedApiVersion();
        var location = requestedVersion is null
            ? "/api/auth/me"
            : $"/api/v{requestedVersion.MajorVersion}/auth/me";

        return Created(location, response);
    }

    /// <summary>Verifies a local account and returns a short-lived access token.</summary>
    /// <remarks>All failed sign-ins return the same response to avoid disclosing whether an account exists.</remarks>
    /// <response code="200">The credentials were accepted and an access token was issued.</response>
    /// <response code="400">The request body is invalid.</response>
    /// <response code="401">The credentials are invalid or the account is temporarily locked.</response>
    /// <response code="403">A configured multi-factor challenge must be completed before token issuance.</response>
    /// <response code="429">Too many authentication attempts were submitted from this client.</response>
    /// <summary>Resets a password using a valid one-time reset token.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthenticationResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await authenticationService.LoginAsync(request, cancellationToken));
    }

    /// <summary>Requests a password reset link without revealing whether the account exists.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        try { await authenticationService.RequestPasswordResetAsync(request.Email, cancellationToken); }
        catch (InvalidOperationException) { /* Keep account existence private when delivery is unavailable. */ }
        catch (SmtpException) { /* Same generic response for delivery failures. */ }
        return Accepted(new { message = "If an account exists for that email, a reset link will be sent." });
    }

    /// <summary>Resets a password using a valid one-time reset token.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await authenticationService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        return NoContent();
    }

    /// <summary>Gets the safe profile for the bearer-token subject.</summary>
    /// <response code="200">Returns the signed-in user's safe profile.</response>
    /// <response code="401">A valid bearer token is required.</response>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthenticatedUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticatedUserDto>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var user = await authenticationService.GetCurrentUserAsync(subject.UserId, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>Gets completed notifications addressed to the bearer-token user.</summary>
    /// <remarks>
    /// This endpoint derives the account identifier and recipient email from
    /// the signed bearer token. It returns only notifications addressed to the
    /// account after both the associated bulk job and job item complete,
    /// excluding incomplete and other-recipient notifications.
    /// </remarks>
    /// <response code="200">Returns the current user's notifications, including those related to orders.</response>
    /// <response code="401">A valid bearer token is required.</response>
    [Authorize]
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(IEnumerable<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetCurrentUserNotifications(CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var currentUser = await authenticationService.GetCurrentUserAsync(subject.UserId, cancellationToken);
        if (currentUser is null || !string.Equals(currentUser.Email, subject.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized();
        }

        return Ok(await notificationService.GetCompletedNotificationsForRecipientAsync(
            subject.Email,
            cancellationToken));
    }

    /// <summary>Gets one completed notification addressed to the bearer-token user.</summary>
    /// <response code="200">Returns the requested notification.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="404">The notification is incomplete, belongs to another recipient, or does not exist.</response>
    [Authorize]
    [HttpGet("notifications/{id:int}")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> GetCurrentUserNotification(
        int id,
        CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var currentUser = await authenticationService.GetCurrentUserAsync(subject.UserId, cancellationToken);
        if (currentUser is null || !string.Equals(currentUser.Email, subject.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized();
        }

        var notification = await notificationService.GetCompletedNotificationForRecipientAsync(
            id, subject.Email, cancellationToken);
        return notification is null ? NotFound() : Ok(notification);
    }

    /// <summary>Marks one completed notification owned by the bearer-token user as read.</summary>
    /// <response code="200">Returns the notification after marking it as read.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="404">The notification is incomplete, belongs to another recipient, or does not exist.</response>
    [Authorize]
    [HttpPatch("notifications/{id:int}/read")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> MarkCurrentUserNotificationAsRead(
        int id,
        CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var notification = await notificationService.MarkCompletedNotificationAsReadForRecipientAsync(
            id, subject.Email, cancellationToken);
        return notification is null ? NotFound() : Ok(notification);
    }

    /// <summary>Deletes one completed notification owned by the bearer-token user.</summary>
    /// <remarks>
    /// The recipient is derived from the bearer token. The shared bulk job,
    /// its job items, and notifications belonging to other recipients remain intact.
    /// </remarks>
    /// <response code="204">The current user's notification was deleted.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="404">The notification is incomplete, belongs to another recipient, or does not exist.</response>
    [Authorize]
    [HttpDelete("notifications/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCurrentUserNotification(
        int id,
        CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var deleted = await notificationService.DeleteCompletedNotificationForRecipientAsync(
            id,
            subject.Email,
            cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Gets the cart belonging to the bearer-token user.</summary>
    /// <remarks>The visitor email is derived from the signed JWT; this route accepts no email parameter.</remarks>
    /// <response code="200">Returns only the signed-in user's cart.</response>
    /// <response code="401">A valid bearer token is required.</response>
    [Authorize]
    [HttpGet("cart")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CartDto>> GetCurrentUserCart(CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        return Ok(await cartService.GetCartForAuthenticatedUserAsync(subject.Email, cancellationToken));
    }

    /// <summary>Adds a product to the cart belonging to the bearer-token user.</summary>
    /// <remarks>The request contains product and quantity only. The server derives the visitor email from the JWT.</remarks>
    /// <response code="200">Returns the signed-in user's updated cart.</response>
    /// <response code="400">The product id or quantity is invalid.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="404">The requested product does not exist.</response>
    [Authorize]
    [HttpPost("cart/items")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartDto>> AddCurrentUserCartItem(
        AddCurrentUserCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var cart = await cartService.AddItemForAuthenticatedUserAsync(subject.Email, request, cancellationToken);
        return cart is null ? NotFound() : Ok(cart);
    }

    /// <summary>Updates an item in the cart belonging to the bearer-token user.</summary>
    /// <response code="200">Returns the signed-in user's updated cart.</response>
    /// <response code="400">The requested quantity is invalid.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="404">The cart item does not exist or belongs to another user.</response>
    [Authorize]
    [HttpPut("cart/items/{cartItemId:int}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartDto>> UpdateCurrentUserCartItem(
        int cartItemId,
        UpdateCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var cart = await cartService.UpdateItemForAuthenticatedUserAsync(
            cartItemId,
            subject.Email,
            request,
            cancellationToken);
        return cart is null ? NotFound() : Ok(cart);
    }

    /// <summary>Removes an item from the cart belonging to the bearer-token user.</summary>
    /// <response code="204">The signed-in user's cart item was removed.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="404">The cart item does not exist or belongs to another user.</response>
    [Authorize]
    [HttpDelete("cart/items/{cartItemId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveCurrentUserCartItem(int cartItemId, CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var removed = await cartService.RemoveItemForAuthenticatedUserAsync(
            cartItemId,
            subject.Email,
            cancellationToken);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>Gets the signed-in account's order history.</summary>
    /// <remarks>The account id is read from the JWT subject; no user id or visitor email is accepted from the client.</remarks>
    /// <response code="200">Returns only orders owned by the signed-in account.</response>
    /// <response code="401">A valid bearer token is required.</response>
    [Authorize]
    [HttpGet("orders")]
    [ProducesResponseType(typeof(IEnumerable<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetCurrentUserOrders(CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        return Ok(await orderService.GetOrdersForUserAsync(subject.UserId, cancellationToken));
    }

    /// <summary>Gets one order owned by the signed-in account.</summary>
    /// <response code="200">Returns the requested account-owned order.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="404">The order does not belong to the signed-in account or does not exist.</response>
    [Authorize]
    [HttpGet("orders/{id:int}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetCurrentUserOrder(int id, CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var order = await orderService.GetOrderForUserAsync(id, subject.UserId, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Deletes an order owned by the signed-in account.</summary>
    /// <response code="204">The order was deleted.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="404">The order does not belong to the signed-in account or does not exist.</response>
    [Authorize]
    [HttpDelete("orders/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCurrentUserOrder(int id, CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var deleted = await orderService.DeleteOrderForUserAsync(id, subject.UserId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Creates an order from the signed-in account's current cart.</summary>
    /// <remarks>
    /// The user id and visitor email are derived from the signed bearer token.
    /// The request never accepts an account id or email, so an authenticated
    /// caller cannot create an order in another account's name.
    /// Supply an optional X-Idempotency-Key to replay the original checkout
    /// response safely after a timeout or lost response.
    /// </remarks>
    /// <response code="201">The account-owned order was created and notification handoff metadata is returned in response headers.</response>
    /// <response code="400">The signed-in account's cart is empty or the idempotency key is invalid.</response>
    /// <response code="401">A valid bearer token is required.</response>
    /// <response code="409">The idempotency key was already used for a different checkout request.</response>
    /// <response code="503">The durable notification job store is at capacity or temporarily unavailable.</response>
    [Authorize]
    [HttpPost("orders")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderDto>> CreateCurrentUserOrder(
        [FromHeader(Name = IdempotencyContext.HeaderName)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var subject = GetAuthenticatedSubject();
        if (subject is null)
        {
            return Unauthorized();
        }

        var requestedVersion = HttpContext.GetRequestedApiVersion();
        var idempotency = IdempotencyContext.Create(
            $"auth.orders.create.v{requestedVersion?.MajorVersion ?? 2}",
            subject.UserId.ToString("D"),
            idempotencyKey);
        var result = await orderService.CreateOrderForUserAsync(
            subject.UserId,
            subject.Email,
            idempotency,
            cancellationToken);
        if (result.IdempotencyReplayed)
        {
            Response.Headers[IdempotencyContext.ReplayHeaderName] = "true";
        }

        ApplyNotificationHandoffHeaders(result);
        return Created(GetCurrentUserOrderLocation(result.Order.Id), result.Order);
    }

    private void ApplyNotificationHandoffHeaders(CreateOrderResult result)
    {
        Response.Headers["X-Notification-Handoff"] = result.NotificationHandoff.ToString();
        if (result.NotificationJob is not { } job)
        {
            return;
        }

        var requestedVersion = HttpContext.GetRequestedApiVersion();
        var statusLocation = requestedVersion is null
            ? $"/api/notifications/bulk/{job.JobId}"
            : $"/api/v{requestedVersion.MajorVersion}/notifications/bulk/{job.JobId}";
        Response.Headers["X-Notification-Job-ID"] = job.JobId.ToString("D");
        Response.Headers["X-Correlation-ID"] = job.CorrelationId;
        Response.Headers["Link"] = $"<{statusLocation}>; rel=\"notification-status\"";
    }

    private string GetCurrentUserOrderLocation(int orderId)
    {
        var requestedVersion = HttpContext.GetRequestedApiVersion();
        return requestedVersion is null
            ? $"/api/auth/orders/{orderId}"
            : $"/api/v{requestedVersion.MajorVersion}/auth/orders/{orderId}";
    }

    private AuthenticatedSubject? GetAuthenticatedSubject()
    {
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;

        return Guid.TryParse(userIdValue, out var userId)
            && userId != Guid.Empty
            && !string.IsNullOrWhiteSpace(email)
                ? new AuthenticatedSubject(userId, email.Trim())
                : null;
    }

    private sealed record AuthenticatedSubject(Guid UserId, string Email);
}
