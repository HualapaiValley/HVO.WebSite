using HVO.Core.Results;
using Microsoft.Extensions.Logging;

namespace HVO.NinaClient.Resilience;

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitBreakerState
{
    Closed,    // Normal operation
    Open,      // Circuit is open, failing fast
    HalfOpen   // Testing if service has recovered
}

/// <summary>
/// Circuit breaker for fault tolerance
/// </summary>
public sealed class CircuitBreaker : IDisposable
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly ILogger? _logger;
    private readonly object _lock = new();
    
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime _nextAttempt = DateTime.MinValue;
    private bool _disposed;

    public CircuitBreaker(int failureThreshold, TimeSpan timeout, ILogger? logger = null)
    {
        _failureThreshold = failureThreshold;
        _timeout = timeout;
        _logger = logger;
        
        _logger?.LogDebug("Circuit breaker initialized - FailureThreshold: {FailureThreshold}, Timeout: {Timeout}", 
            failureThreshold, timeout);
    }

    /// <summary>
    /// Gets the current state of the circuit breaker
    /// </summary>
    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Gets the current failure count
    /// </summary>
    public int FailureCount
    {
        get
        {
            lock (_lock)
            {
                return _failureCount;
            }
        }
    }

    /// <summary>
    /// Execute an operation through the circuit breaker
    /// 
    /// Circuit Breaker State Machine:
    /// - CLOSED: Normal operation, all calls proceed
    /// - OPEN: Service is failing, fail fast without calling operation  
    /// - HALF-OPEN: Testing if service has recovered, allow one call through
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="operation">Operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    public async Task<Result<T>> ExecuteAsync<T>(Func<Task<Result<T>>> operation, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CircuitBreaker));

        // Check current circuit breaker state and decide whether to proceed
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Open:
                    // Circuit is open due to too many failures
                    // Check if enough time has passed to test recovery
                    if (DateTime.UtcNow < _nextAttempt)
                    {
                        // Still in timeout period - fail fast without calling the operation
                        _logger?.LogDebug("Circuit breaker is open, failing fast");
                        return Result<T>.Failure(new InvalidOperationException("Circuit breaker is open"));
                    }
                    
                    // Timeout period has elapsed - transition to half-open to test if service recovered
                    _state = CircuitBreakerState.HalfOpen;
                    _logger?.LogInformation("Circuit breaker transitioning from Open to HalfOpen");
                    break;

                case CircuitBreakerState.HalfOpen:
                    // Testing recovery - allow this operation to proceed
                    // Success will close the circuit, failure will re-open it
                    break;

                case CircuitBreakerState.Closed:
                    // Normal operation - all calls proceed
                    break;
            }
        }

        try
        {
            // Execute the operation (could be an API call, database query, etc.)
            var result = await operation();

            // Update circuit breaker state based on operation result
            if (result.IsSuccessful)
            {
                OnSuccess(); // Reset failure count and potentially close circuit
            }
            else
            {
                OnFailure(result.Error); // Increment failure count and potentially open circuit
            }

            return result;
        }
        catch (Exception ex)
        {
            // Unexpected exception during operation - treat as failure
            OnFailure(ex);
            return Result<T>.Failure(ex);
        }
    }

    /// <summary>
    /// Handle successful operation - reset failure count and potentially close circuit
    /// </summary>
    private void OnSuccess()
    {
        lock (_lock)
        {
            // Reset failure count on any successful operation
            _failureCount = 0;
            
            // If we were in half-open state (testing recovery), close the circuit
            if (_state == CircuitBreakerState.HalfOpen)
            {
                _state = CircuitBreakerState.Closed;
                _logger?.LogInformation("Circuit breaker closed after successful operation");
            }
        }
    }

    /// <summary>
    /// Handle failed operation - increment failure count and potentially open circuit
    /// </summary>
    private void OnFailure(Exception? exception)
    {
        lock (_lock)
        {
            // Increment failure count for each failure
            _failureCount++;
            _logger?.LogWarning(exception, "Circuit breaker failure #{FailureCount}", _failureCount);

            // Check if we've reached the failure threshold to open the circuit
            if (_failureCount >= _failureThreshold)
            {
                // Open the circuit - future calls will fail fast until timeout expires
                _state = CircuitBreakerState.Open;
                _nextAttempt = DateTime.UtcNow.Add(_timeout); // Set when we can test recovery
                _logger?.LogError("Circuit breaker opened after {FailureCount} failures. Next attempt at {NextAttempt}", 
                    _failureCount, _nextAttempt);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _logger?.LogDebug("Circuit breaker disposed");
    }
}

/// <summary>
/// Retry policy implementation
/// </summary>
public static class RetryPolicy
{
    /// <summary>
    /// Execute operation with exponential backoff retry
    /// 
    /// Retry Logic:
    /// - Attempts up to maxAttempts times
    /// - Uses exponential backoff: delay doubles each retry (with jitter)
    /// - Only retries certain exception types (network issues, timeouts)
    /// - Stops immediately for non-retryable errors (invalid arguments, cancellation)
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="operation">Operation to execute</param>
    /// <param name="maxAttempts">Maximum retry attempts</param>
    /// <param name="baseDelay">Base delay between retries</param>
    /// <param name="logger">Optional logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the operation</returns>
    public static async Task<Result<T>> ExecuteWithRetryAsync<T>(
        Func<Task<Result<T>>> operation,
        int maxAttempts,
        TimeSpan baseDelay,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxAttempts)
        {
            try
            {
                // Execute the operation
                var result = await operation();
                
                if (result.IsSuccessful)
                {
                    // Operation succeeded
                    if (attempt > 0)
                    {
                        logger?.LogInformation("Operation succeeded on attempt {Attempt}", attempt + 1);
                    }
                    return result;
                }

                // Operation failed - check if we should retry
                lastException = result.Error;
                
                if (ShouldRetry(result.Error, attempt, maxAttempts))
                {
                    // Wait with exponential backoff before next attempt
                    await DelayForAttempt(attempt, baseDelay, logger, cancellationToken);
                    attempt++;
                    continue;
                }
                
                // Don't retry this error - return failure immediately
                return result;
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt, maxAttempts))
            {
                // Exception occurred but it's retryable
                lastException = ex;
                await DelayForAttempt(attempt, baseDelay, logger, cancellationToken);
                attempt++;
            }
            catch (Exception ex)
            {
                // Exception occurred and it's not retryable
                logger?.LogError(ex, "Operation failed on attempt {Attempt} - not retrying", attempt + 1);
                return Result<T>.Failure(ex);
            }
        }

        // All retry attempts exhausted
        logger?.LogError(lastException, "Operation failed after {MaxAttempts} attempts", maxAttempts);
        return Result<T>.Failure(lastException ?? new InvalidOperationException("Operation failed after all retry attempts"));
    }

    /// <summary>
    /// Determines if an exception should trigger a retry attempt
    /// 
    /// Retry Strategy:
    /// - Never retry: Cancellation, invalid arguments (client errors)
    /// - Always retry: Network issues, timeouts, API errors (potentially transient)
    /// </summary>
    private static bool ShouldRetry(Exception? exception, int attempt, int maxAttempts)
    {
        // Don't retry if we've reached the maximum attempts
        if (attempt >= maxAttempts - 1)
            return false;

        return exception switch
        {
            TaskCanceledException => false,     // Operation was cancelled - don't retry
            ArgumentException => false,         // Invalid arguments - won't succeed on retry
            HttpRequestException => true,       // Network issues - potentially transient
            TimeoutException => true,           // Request timeout - might succeed on retry
            InvalidOperationException => true,  // API logical errors - might be transient
            _ => true                          // Default: retry other exceptions
        };
    }

    /// <summary>
    /// Calculate delay for retry attempt using exponential backoff with jitter
    /// 
    /// Delay Formula: baseDelay * 2^attempt + random(0-1000ms)
    /// Example with 1000ms base: 1s, 2s, 4s, 8s, 16s, ... (plus jitter)
    /// Jitter prevents "thundering herd" when multiple clients retry simultaneously
    /// </summary>
    private static async Task DelayForAttempt(int attempt, TimeSpan baseDelay, ILogger? logger, CancellationToken cancellationToken)
    {
        // Exponential backoff: delay doubles each attempt
        // Add random jitter (0-1000ms) to prevent synchronized retries across multiple clients
        var delay = TimeSpan.FromMilliseconds(
            baseDelay.TotalMilliseconds * Math.Pow(2, attempt) +  // Exponential component
            Random.Shared.Next(0, 1000));                        // Jitter component

        logger?.LogDebug("Retrying operation in {Delay}ms (attempt {Attempt})", delay.TotalMilliseconds, attempt + 1);
        
        await Task.Delay(delay, cancellationToken);
    }
}