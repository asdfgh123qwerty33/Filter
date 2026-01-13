using API大專.DTO;
using API大專.Models;
using API大專.service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace API.Controllers
{
    [Route("Commission")]
    public class CreateCommissionController : ControllerBase
    {
        private readonly ProxyContext _proxyContext;
        private readonly CommissionService _CommissionService;
        public CreateCommissionController(ProxyContext proxyContext, CommissionService commissionService)
        {
            _proxyContext = proxyContext;
            _CommissionService = commissionService;
        }

        //新增委託 -> 錢包確認 扣款
        [HttpPost]
        public async Task<IActionResult> CreateCommission([FromForm] CommissionCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    success = false,
                    errors = ModelState
                     .Where(x => x.Value.Errors.Count > 0)
                     .ToDictionary(k => k.Key, //欄位名稱
                     v => v.Value.Errors.Select(e => e.ErrorMessage).ToArray() 
                     )
                });
            }
            // 取得目前登入id
            var userid = "101";
            //var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value; // 如果是 JWT / Session用這個
            var user = await _proxyContext.Users
    .FirstOrDefaultAsync(u => u.Uid == userid);
             
           
                if (user == null)
                {
                return Unauthorized(new { success = false, message = "請先登入！" });
            }
            //手續費 跟 總價 四捨五入
            decimal fee = 0.1m;
            decimal Pricefee = (dto.Price* dto.Quantity) * fee; //平台手續費
            decimal TotalPrice = Math.Round((dto.Price * dto.Quantity) + Pricefee,
                                                    0, MidpointRounding.AwayFromZero);

            //判斷錢包
            if (user.Balance < TotalPrice)
            {
                return BadRequest(new
                {
                    success = false,
                    code = "BALANCE_NOT_ENOUGH",
                    message = "錢包餘額不足"
                });
            }
            // 扣錢
            using var transaction = await _proxyContext.Database.BeginTransactionAsync();
            try
            {
                user.Balance -= TotalPrice;

                //圖片上傳
                string? imageUrl = null;
                string? FilePath = null;

                if (dto.Image != null && dto.Image.Length > 0)
                {
                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(dto.Image.FileName)}";
                    FilePath = Path.Combine("wwwroot", "uploads", fileName);

                    imageUrl = $"/uploads/{fileName}";
                }

                var Commission = new Commission
                {
                    //creator_id = userId;            // 從 JWT / Session
                    //CreatorId = userid, // 抓session的id
                    CreatorId = userid,//測試
                    Title = dto.Title,
                    Description = dto.Description,
                    Price = dto.Price,
                    Fee = Pricefee, //平台手續費
                    Quantity = dto.Quantity,
                    Category = dto.Category,
                    Location = dto.Location,
                    EscrowAmount = TotalPrice, //Commission 委託 扣住金額
                    Deadline = dto.Deadline.AddDays(7), //結束日期自動加7天 還要審核

                    Status = "審核中",                                  // 預設
                    CreatedAt = DateTime.Now,              // 後端補
                    ImageUrl = imageUrl
                };
               
                _proxyContext.Commissions.Add(Commission);
                await _proxyContext.SaveChangesAsync();

                // 記錄歷史
                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var history = new CommissionHistory
                {
                    CommissionId = Commission.CommissionId,
                    Action = "CREATE",
                    ChangedBy = userid,
                    ChangedAt = DateTime.Now,
                    OldData = null,
                    NewData = JsonSerializer.Serialize(new
                    {
                        Commission.Title,
                        Commission.Description,
                        Commission.Price,
                        Commission.Quantity,
                        Commission.Category,
                        Commission.Deadline,
                        Commission.Location,
                    }, jsonOptions)
                };

                _proxyContext.CommissionHistories.Add(history);
                await _proxyContext.SaveChangesAsync();

                if (dto.Image != null && FilePath != null)  
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                    using var stream = new FileStream(FilePath, FileMode.Create);
                    await dto.Image.CopyToAsync(stream);
                }

                await transaction.CommitAsync();
                //回傳的資料
                var response = new CommissionDataDto
                {
                    Title = Commission.Title,
                    Description = Commission.Description,
                    TotalPrice = TotalPrice,
                    Quantity = Commission.Quantity,
                    Category = Commission.Category,
                    Location = Commission.Location,
                    Status = Commission.Status,

                    CreatedAt = DateTime.Now,
                    Deadline = dto.Deadline.AddDays(7),
                    ImageUrl = Commission.ImageUrl
                };

                return Ok(new
                {
                    success = true,
                    data = response
                });
            }
            //catch (Exception ex) //測試用
            //{
            //    await transaction.RollbackAsync();

            //    return StatusCode(500, new
            //    {
            //        success = false,
            //        message = ex.Message,
            //        inner = ex.InnerException?.Message,
            //        stack = ex.StackTrace
            //    });
            //}
            catch
            {
                await transaction.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message = "建立委託失敗，請稍後再試"
                });
            }
        }


        //編輯委託
        [HttpPut("{id}")]
        public async Task<IActionResult> EditCommission(int id, [FromForm] CommissionEditDto dto)
        {
            if (!ModelState.IsValid) {
                return BadRequest(new
                {
                    success = false,
                    errors = ModelState
                                    .Where(x => x.Value.Errors.Count > 0)
                                    .ToDictionary(
                                    k => k.Key,
                                    v => v.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                                )
                });
            }


            //id = 11; //模擬Commission id
            // 模擬user  之後要改session
            var userid = "101";

            var (success, message) = await _CommissionService
                                                         .EditCommissionAsync(id, userid, dto);
            if (!success) {
                        return BadRequest(new { 
                                success = false,
                                message = message
                        });        
                }

            return Ok(new
            {
                success = true,
                message = message
            });
        }

       
    }
}
