using System.ClientModel;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Classifies a failed agent run as transient (worth another attempt) or not. By the time one of
/// these reaches us the transport has already retried the single request with backoff, so this is
/// the "provider is having a bad minute" class: network faults, timeouts, 408/429/5xx.
/// </summary>
public static class TransientFailure
{
    public static bool IsTransient(Exception? exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case HttpRequestException:
                case TimeoutException:
                case IOException:
                // Reaching here means OUR token was not cancelled (the coordinator handles that case
                // first): the cancellation came from inside a dependency, i.e. a request timeout.
                case OperationCanceledException:
                case ClientResultException { Status: 0 or 408 or 429 or >= 500 }:
                    return true;
            }
        }

        return false;
    }
}
