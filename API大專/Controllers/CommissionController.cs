using API大專.DTO;
using API大專.Models;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace API大專.Controllers
{
    [ApiController]
    [Route("api/Commissions")]
    public class CommissionController : ControllerBase
    {
        private readonly ProxyContext _proxyContext;
        public CommissionController(ProxyContext proxyContext)
        {
            _proxyContext = proxyContext;
        }

        //委託 展示
        [HttpGet]
        public async Task<IActionResult> GetCommissionsList() 
        { 
            var commissions = await _proxyContext.Commissions
                                                .Where(u=>u.Status == "待接單")
                                                .OrderByDescending(u=>u.UpdatedAt) 
                                                .Select(u=> new 
                                                { 
                                                u.CommissionId,
                                                u.Title,
                                                u.Price,
                                                u.Quantity,
                                                u.Location,
                                                u.Category,
                                                u.ImageUrl,
                                                u.Deadline,
                                                u.Status
                                                }).ToListAsync();

            return Ok(new
            {
                success = true,
                data = commissions
            });
        }

        //撈單筆詳細資料
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetDetail(int id) 
        {
            var Commission = await _proxyContext.Commissions
                                               .Where(c => c.CommissionId == id && c.Status == "待接單")
                                               .Select(c => new
                                               {    //比普通清單多
                                                   c.CommissionId,
                                                   c.Title,
                                                   c.Description, //描述
                                                   c.Price, 
                                                   c.Quantity,
                                                   c.Fee,                //平台手續費
                                                   c.EscrowAmount, // 會拿到的總價格
                                                   c.Category,
                                                   c.Location,
                                                   c.ImageUrl,
                                                   c.CreatedAt, //這委託建立的時間
                                                   c.Deadline,
                                                   c.Status        //顯示用
                                               }).FirstOrDefaultAsync();
            if (Commission==null) {
                return NotFound(
                            new
                            {
                                success = false,
                                message = "找不到此委託"
                            } );
            }
            return Ok(new
            {
                success = true,
                data = Commission
            });
        
        }

  

        [HttpPost("{id:int}/accept")]
        public async Task<IActionResult> acceptCommission(int id)
        {
            var userid = "102";
            //var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);

            using var transaction = await _proxyContext.Database.BeginTransactionAsync();
            try
            {
                var oldDiff = await _proxyContext.Commissions
                             .Where(c => c.CommissionId == id)
                             .Select(c => new
                             {
                                 oldstatus =  c.Status
                             }).FirstOrDefaultAsync();
                var affected = await _proxyContext.Database.ExecuteSqlRawAsync(@"
                          UPDATE Commission
                          SET Status = '已接單',
                          UpdatedAt = GETDATE()
                          WHERE 
                          commission_id = @id
                          AND status = '待接單'
                          AND creator_id <> @userId
                            ",
                    new SqlParameter("@id", id),
                    new SqlParameter("@userId", userid)
                    );

                if (affected == 0)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        success = false,
                        message = "訂單已被接取或無法接單"
                    });
                }

                var commission = await _proxyContext.Commissions
                                                  .Where(c => c.CommissionId == id)
                                                  .Select(c => new
                                                  {
                                                      c.CommissionId,
                                                      c.CreatorId,
                                                      c.EscrowAmount,
                                                      c.Status
                                                  }).FirstOrDefaultAsync();

                var order = new CommissionOrder
                {
                    CommissionId = commission.CommissionId,
                    SellerId = userid,
                    BuyerId = commission.CreatorId,
                    Status = "PENDING", //未完成
                    Amount = commission.EscrowAmount,
                    CreatedAt = DateTime.Now
                };
                 _proxyContext.CommissionOrders.Add(order);

                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var newDiff = await _proxyContext.Commissions
                              .Where(c => c.CommissionId == id)
                              .Select(c => new
                              {
                                  newstatus = c.Status
                              }).FirstOrDefaultAsync();
                var history = new CommissionHistory
                    {
                        CommissionId = commission.CommissionId,
                        Action = "ACCEPT",
                        ChangedBy = userid,
                        ChangedAt = DateTime.Now,
                        OldData = JsonSerializer.Serialize(oldDiff, jsonOptions),
                        NewData = JsonSerializer.Serialize(newDiff, jsonOptions)
                    };

                
                    _proxyContext.CommissionHistories.Add(history);
                

                await _proxyContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "訂單接受"
                });

            }
            //catch
            //{
            //    await transaction.RollbackAsync();
            //    return BadRequest(new
            //    {
            //        success = false,
            //        message = "接取訂單失敗，或是訂單已被接取"
            //    });
            //}
            catch (Exception ex) //如果報錯可以用
            {
                await transaction.RollbackAsync();
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }


        }

    }
}
