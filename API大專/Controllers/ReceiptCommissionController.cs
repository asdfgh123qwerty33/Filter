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
    [Route("api/commissions")]
    public class ReciptCommission : ControllerBase
    {
        private readonly ProxyContext _proxyContext;
        public ReciptCommission(ProxyContext proxyContext)
        {
            _proxyContext = proxyContext;
        }

        //上傳明細
        [HttpPost("{id:int}/receipt")]
        public async Task<IActionResult> UploadReceipt(  int id, [FromForm] UploadReceiptDto dto)
        {
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? "102";// 接單者

            using var tx = await _proxyContext.Database.BeginTransactionAsync();

            var order = await _proxyContext.CommissionOrders
                .FirstOrDefaultAsync(o => o.CommissionId == id && o.SellerId == userId);

            if (order == null)
                return Forbid("你不是接單者");

            var commission = await _proxyContext.Commissions
                .FirstOrDefaultAsync(c => c.CommissionId == id);
            if (commission == null )
            {
                return NotFound("委託不存在");
            }
            if (dto.Image == null) { return BadRequest("請上傳圖片"); }

            if (commission.Status != "已接單" && commission.Status != "出貨中")
                return BadRequest("目前狀態不可上傳明細");


            var commissionReceipt = await _proxyContext.CommissionReceipts
                                   .FirstOrDefaultAsync(c => c.CommissionId == id);
            bool isFirstUpload = commissionReceipt == null;
            var oldremark = commissionReceipt.Remark;


            // 存圖片
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(dto.Image.FileName)}";
            var path = Path.Combine("wwwroot", "receipts", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var stream = new FileStream(path, FileMode.Create);
            await dto.Image.CopyToAsync(stream);

            var newImageUrl = $"/receipts/{fileName}";
            
            if (isFirstUpload)
            {
                commissionReceipt = new CommissionReceipt
                {
                    CommissionId = id,
                    UploadedBy = userId
                };
                _proxyContext.CommissionReceipts.Add(commissionReceipt);
            }

            // 不管是不是第一次，都是更新「同一筆」
            commissionReceipt.ReceiptImageUrl = newImageUrl;
            commissionReceipt.ReceiptAmount = dto.ReceiptAmount;
            commissionReceipt.ReceiptDate = dto.ReceiptDate;
            commissionReceipt.Remark = dto.Remark;

            var oldStatus = commission.Status;
            if (commission.Status == "已接單")
            {
                commission.Status = "出貨中";
                commission.UpdatedAt = DateTime.Now;
            }


            var oldDiff = new Dictionary<string, object>();
            var newDiff = new Dictionary<string, object>();


            oldDiff["imageurl"] = (isFirstUpload == true ? "null" : commissionReceipt.ReceiptImageUrl);
            newDiff["imageurl"] = newImageUrl;

            //已接單在上面變為出貨中(第一次)，所以紀錄，後續再重傳都是出貨中所以只記錄圖片變化
            if (oldStatus != commission.Status)
            {
                oldDiff["status"] = oldStatus;
                newDiff["status"] = "出貨中";
            }
            if (oldremark != dto.Remark)
            {
                oldDiff["remark"] = oldremark;
                newDiff["remark"] = dto.Remark;
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
                    Action = (oldStatus == "已接單" ? "UPLOAD_RECEIPT" : "REUPLOAD_RECEIPT"),
                    ChangedBy = userId,
                    ChangedAt = DateTime.Now,
                    OldData = JsonSerializer.Serialize(oldDiff, jsonOptions),
                    NewData = JsonSerializer.Serialize(newDiff, jsonOptions)
                });
            }
         
            await _proxyContext.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { success = true, message = (oldStatus == "已接單"  ? "明細上傳成功" : "明細重新上傳成功" ) });
        }


        [HttpPost("{id:int}/ship")]
        public async Task<IActionResult> ShipCommission( int id,[FromBody] CommissionShipDto dto)
        {
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? "102"; // Swagger 測試用

            using var tx = await _proxyContext.Database.BeginTransactionAsync();

            // 1️ 驗證接單者
            var order = await _proxyContext.CommissionOrders
                .FirstOrDefaultAsync(o => o.CommissionId == id && o.SellerId == userId);

            if (order == null)
                return Forbid("你不是接單者");

            // 2️ 驗證委託
            var commission = await _proxyContext.Commissions
                .FirstOrDefaultAsync(c => c.CommissionId == id);

            if (commission == null)
                return NotFound("委託不存在");

            if (commission.Status != "出貨中")
                return BadRequest("目前狀態不可更改");

            // 3️ 取得寄貨資料（只會有一筆）
            var shipping = await _proxyContext.CommissionShippings
                .FirstOrDefaultAsync(s => s.CommissionId == id);

            var oldTrackingNumber = shipping?.TrackingNumber; //舊的nunber
            var oldLogistics = shipping?.LogisticsName;//舊的Name       
            var oldstatus = commission.Status; //出貨中

            bool isFirstShip = shipping == null;

            if (isFirstShip)
            {     
                shipping = new CommissionShipping
                {
                    CommissionId = id,
                    ShippedBy = userId,
                    Status = "已寄出"
                };
                _proxyContext.CommissionShippings.Add(shipping);
            }
            if (commission.Status != "已寄出")
            {
                commission.Status = "已寄出";
                commission.UpdatedAt = DateTime.Now;
            }

            // 4️ 更新寄貨資訊
            shipping.Status = "已寄出";
            shipping.ShippedAt = DateTime.Now;
            shipping.LogisticsName = dto.LogisticsName;
            shipping.TrackingNumber = dto.TrackingNumber;
            shipping.Remark = dto.Remark;

           

            // 5History diff
            var oldDiff = new Dictionary<string, object>();
            var newDiff = new Dictionary<string, object>();

            oldDiff["shipping_status"] = isFirstShip ? "出貨中" : "已寄出";
            newDiff["shipping_status"] = "已寄出";
            if(oldLogistics != dto.LogisticsName)
            { 
            oldDiff["logistics"] = oldLogistics;
            newDiff["logistics"] = dto.LogisticsName;
            }
            if (oldTrackingNumber != dto.TrackingNumber)
            {
                oldDiff["tracking_number"] = oldTrackingNumber;
                newDiff["tracking_number"] = dto.TrackingNumber;
            }
            if (oldstatus != shipping.Status)
            {
                oldDiff["commissionstatus"] = oldstatus; //出貨中
                newDiff["commissionstatus"] = shipping.Status;
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
                    Action = isFirstShip ? "SHIP_COMMISSION" : "RESHIP_COMMISSION",
                    ChangedBy = userId,
                    ChangedAt = DateTime.Now,
                    OldData = JsonSerializer.Serialize(oldDiff, jsonOptions),
                    NewData = JsonSerializer.Serialize(newDiff, jsonOptions)
                });
            }
            await _proxyContext.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                success = true,
                message = isFirstShip ? "寄貨成功" : "寄貨資訊更新成功"
            });
        }

    }
}
