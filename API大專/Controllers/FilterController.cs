using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using API大專.Models;
namespace API大專.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FilterController : ControllerBase
    {
        private ProxyContext _context;

        public FilterController(ProxyContext context)
        {
            _context = context;
        }

        // 根據地點(location)篩選委託單，並可依價格或截止日期排序
        [HttpGet("location/{location}")]
        public IActionResult GetByLocation(string location, [FromQuery] string? sort = null)
        {
            var query = _context.Commissions                      // 根據location參數過濾
                                  .Where(c => c.Location != null && c.Location.Contains(location) && c.Status == "待接單");
            // 根據sort參數決定排序
            query = sort switch
                                  { 
                                      "price_asc" => query.OrderBy(c => c.Price),  // price_asc配價格由低到高的按鈕
                                      "price_desc" => query.OrderByDescending(c => c.Price), // price_desc配價格由高到低的按鈕
                                      "deadline_asc" => query.OrderBy(c => c.Deadline), // deadline_asc配截止日期由近到遠的按鈕
                                      "deadline_desc" => query.OrderByDescending(c => c.Deadline), // deadline_desc配截止日期由遠到近的按鈕
                                      _ => query.OrderByDescending(c => c.CreatedAt) // 預設由新到舊排
                                  };

            var results = query.Select(c => new
                                  {
                                      c.Title,
                                      c.Price,
                                      c.Quantity,
                                      c.Location,
                                      c.Category,
                                      c.ImageUrl,
                                      c.Deadline,
                                  }).Take(30)  // 最多回傳30筆
                                  .ToList();
            return Ok(new
            {
                success = true,
                data = results
            });
            }

        // 取得前五名熱門地點
        [HttpGet("top5-locations")]
            public IActionResult GetTopLocations()
            {
                var topLocations = _context.Commissions
                    // 1. 依照地點分組
                    .GroupBy(c => c.Location)
                    // 2. 轉換成匿名物件，包含地點名稱與該地點的筆數
                    .Select(g => new
                    {
                        Location = g.Key,
                        Count = g.Count()
                    })
                    // 3. 依照筆數由高到低排序
                    .OrderByDescending(x => x.Count)
                    // 4. 只取前五名
                    .Take(5)
                    .ToList();

                return Ok(new
                {
                    success = true,
                    data = topLocations
                });
        }
    }
}
