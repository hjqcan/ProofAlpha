using Autotrade.Application.DTOs;
using Autotrade.Application.Exceptions;

namespace Autotrade.Trading.Tests;

public sealed class ApplicationPrimitivesTests
{
    [Fact]
    public void PagedRequestDto_ClampsInvalidPagingValues()
    {
        var request = new PagedRequestDto
        {
            PageIndex = 0,
            PageSize = 5_000
        };

        Assert.Equal(1, request.PageIndex);
        Assert.Equal(1_000, request.PageSize);
        Assert.Equal(0, request.Skip);
    }

    [Fact]
    public void PagedResultDto_ComputesNavigationProperties()
    {
        var result = new PagedResultDto<string>(
            ["a", "b"],
            totalCount: 12,
            pageIndex: 2,
            pageSize: 5);

        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void ResultDto_FailureCarriesErrorCode()
    {
        var result = ResultDto.FailureResult("failed", "ERR");

        Assert.False(result.Success);
        Assert.Equal("failed", result.Message);
        Assert.Equal("ERR", result.ErrorCode);
    }

    [Fact]
    public void EntityNotFoundException_CarriesStructuredContext()
    {
        var id = Guid.NewGuid();

        var exception = new EntityNotFoundException("Order", id);

        Assert.Equal("ENTITY_NOT_FOUND", exception.ErrorCode);
        Assert.Equal("Order", exception.EntityName);
        Assert.Equal(id, exception.Id);
    }
}
