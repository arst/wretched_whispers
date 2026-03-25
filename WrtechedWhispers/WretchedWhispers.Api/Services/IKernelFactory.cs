#pragma warning disable SKEXP0001

using Microsoft.SemanticKernel;

namespace WretchedWhispers.Api.Services;

public interface IKernelFactory
{
    (Kernel Kernel, string[] RegisteredFunctions) CreateForStage(SessionContext sessionContext, SessionStage stage);
}
