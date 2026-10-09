namespace DistributionApp.Exceptions;

public class NotFoundException(string message) : Exception(message);

public class InsufficientStockException(int productId, int requested)
    : Exception(string.Format("Insufficient stock for product {0} (requested {1}).", productId, requested));

public class InvalidStatusTransitionException(string from, string to)
    : Exception(string.Format("Cannot change status from {0} to {1}.", from, to));

public class BadRequestException(string message) : Exception(message);

public class IdempotencyKeyReusedException(string key)
    : Exception(string.Format("Idempotency-Key '{0}' was already used with a different request payload.", key));