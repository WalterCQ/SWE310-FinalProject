using Microsoft.AspNetCore.Mvc;

namespace TaskFlow.Api.Helpers;

public static class ApiResponseExtensions
{
    public static ActionResult ToActionResult<T>(this ControllerBase controller, ApiResponse<T> response)
    {
        return response.StatusCode switch
        {
            StatusCodes.Status200OK => controller.Ok(response),
            StatusCodes.Status201Created => controller.StatusCode(StatusCodes.Status201Created, response),
            StatusCodes.Status401Unauthorized => controller.Unauthorized(response),
            StatusCodes.Status403Forbidden => controller.StatusCode(StatusCodes.Status403Forbidden, response),
            StatusCodes.Status404NotFound => controller.NotFound(response),
            _ => controller.BadRequest(response)
        };
    }
}
