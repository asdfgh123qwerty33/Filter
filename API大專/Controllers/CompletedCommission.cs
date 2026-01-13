using API大專.Models;
using API大專.service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Any;
using System.Security.Claims;
using System.Text.Json;
using API大專.DTO;

namespace API大專.Controllers
{
    [ApiController]
    [Route("api/Commissions")]
    public class COMPLETEDCommission : Controller
    {
        private readonly ProxyContext _proxyContext;
        public COMPLETEDCommission(ProxyContext proxyContext)
        {
            _proxyContext = proxyContext;
        }

        [HttpGet("MyCompleted")]
        public async Task<ActionResult> GetCompletedOrder(string userid)
        {
            //var userid = "101";
            var userExists = await _proxyContext.Users
                     .AnyAsync(u => u.Uid == userid);
            if (!userExists)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "尚未登入，或是權限不足"
                });
            }
            var Orders = await (
                        from o in _proxyContext.CommissionOrders
                        join c in _proxyContext.Commissions
                        on o.CommissionId equals c.CommissionId
                        join buyerUser in _proxyContext.Users
                        on o.BuyerId equals buyerUser.Uid
                        join sellerUser in _proxyContext.Users
                        on o.SellerId equals sellerUser.Uid
                        where o.Status == "COMPLETED"
                        && (o.BuyerId == userid || o.SellerId ==userid)
                        select new
                        {
                            title = c.Title,
                            imageurl = c.ImageUrl,
                            description = c.Description,
                            location = c.Location,
                            status = o.Status,
                            amount = o.Amount,
                            finishedAt = o.FinishedAt,
                            buyer = new
                            {
                                id = buyerUser.Uid,
                                name = buyerUser.Name
                            },
                            seller = new
                            {
                                id = sellerUser.Uid,
                                name = sellerUser.Name
                            }
                        }
                            ).ToListAsync();
            return Ok(new
            {
                success = true,
                data = Orders
            });

        }

        //完成訂單 (買家)
        [HttpPost("{id:int}/complete")]
        public async Task<IActionResult> CompleteCommission(int id)
        {
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "101";

            using var tx = await _proxyContext.Database.BeginTransactionAsync();

            var commission = await _proxyContext.Commissions
                .FirstOrDefaultAsync(c => c.CommissionId == id);

            if (commission == null)
                return NotFound("委託不存在");

            if (commission.CreatorId != userId)
                return Forbid("你不是此委託的建立者");

            if (commission.Status != "已寄出")
                return BadRequest("目前狀態不可完成");

            var order = await _proxyContext.CommissionOrders
                .FirstOrDefaultAsync(o => o.CommissionId == id);

            if (order == null)
                return BadRequest("訂單紀錄不存在");

            var oldStatus = commission.Status; //已寄出 狀態紀錄

            // 狀態更新
            commission.Status = "已完成";
            commission.UpdatedAt = DateTime.Now;
            order.Status = "COMPLETED";
            order.FinishedAt = DateTime.Now;

            // 金流
            var paymentService = new CommissionPaymentService(_proxyContext);
            await paymentService.ReleaseToSellerAsync(id);

            // History
            var oldDiff = new Dictionary<string, object>();
            var newDiff = new Dictionary<string, object>();
            if (oldStatus != commission.Status) 
            {
                oldDiff["status"] = oldStatus;
                newDiff["status"] = commission.Status;
            }


            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            if (oldDiff.Any())
            {
                _proxyContext.CommissionHistories.Add(new CommissionHistory
                {
                    CommissionId = id,
                    Action = "COMPLETE_COMMISSION",
                    ChangedBy = userId,
                    OldData = JsonSerializer.Serialize(oldDiff, jsonOptions),
                    NewData = JsonSerializer.Serialize(newDiff, jsonOptions)
                });
            }
            await _proxyContext.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { success = true, message = "訂單已完成" });
        }


        //商品瑕疵 取消 委託人必須退貨給 接委託人(承擔成本)
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> CancelCommission(int id, [FromBody] CommissionCancelDto dto)
        {
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "101";

            using var tx = await _proxyContext.Database.BeginTransactionAsync();

            var commission = await _proxyContext.Commissions
                .FirstOrDefaultAsync(c => c.CommissionId == id);

            if (commission == null)
                return NotFound("委託不存在");

            if (commission.CreatorId != userId)
                return Forbid("系統錯誤，你不是此委託者");

            if (commission.Status != "已寄出")
                return BadRequest("目前狀態不可取消");

            var order = await _proxyContext.CommissionOrders
                .FirstOrDefaultAsync(o => o.CommissionId == id);

            if (order == null)
                return BadRequest("訂單紀錄不存在");

            var oldStatus = commission.Status; //紀錄舊狀態

            // 狀態
            commission.Status = "cancelled";
            commission.UpdatedAt = DateTime.Now;
            order.Status = "CANCELLED";
            order.FinishedAt = DateTime.Now;

            // 退款
            var paymentService = new CommissionPaymentService(_proxyContext);
            await paymentService.RefundToBuyerAsync(id);

            // History
            var oldDiff = new Dictionary<string, object>();
            var newDiff = new Dictionary<string, object>();

            if (oldStatus != "cancelled" && oldStatus == "已寄出") 
            {
                oldDiff["status"] = oldStatus;
                newDiff["status"] = commission.Status;
            }
            if (dto.Reason != null)
            {
                oldDiff["Reason"] = null;
                newDiff["Reason"] = dto.Reason;
            }

            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            _proxyContext.CommissionHistories.Add(new CommissionHistory
            {
                CommissionId = id,
                Action = "CANCEL_COMMISSION",
                ChangedBy = userId,
                OldData = JsonSerializer.Serialize(oldDiff, jsonOptions),
                NewData = JsonSerializer.Serialize(newDiff, jsonOptions)
            });

            await _proxyContext.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { success = true, message = "訂單已取消並退款" });
        }






    }
}
