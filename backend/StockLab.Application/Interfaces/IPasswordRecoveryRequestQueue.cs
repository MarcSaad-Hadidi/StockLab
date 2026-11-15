namespace StockLab.Application.Interfaces;

public interface IPasswordRecoveryRequestQueue
{
    bool TryEnqueue(string email, string language);
}
