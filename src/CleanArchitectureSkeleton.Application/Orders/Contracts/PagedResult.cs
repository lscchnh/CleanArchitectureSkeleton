namespace CleanArchitectureSkeleton.Application.Orders;

// Partagé par toutes les slices de LECTURE qui paginent (aujourd'hui : ListOrders uniquement).
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
