using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using API大專.Models;
using API大專.Hubs;
using Microsoft.AspNetCore.SignalR;


namespace API大專.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotifyController : ControllerBase
    {
        private readonly ProxyContext _context;
        private IHubContext<NotificationHub> _hubContext; // 注入 Hub

        public NotifyController(ProxyContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpPost("update-status/{CommissionId}")]
        public async Task<IActionResult> UpdateOrderStatus(int CommissionId, string targetStatus)
        {
            // 呼叫邏輯程式，傳入目標狀態
            var result = await ProcessStatusChangeAsync(CommissionId, targetStatus);

            if (result == "訂單不存在") return NotFound(result);
            return Ok(new { message = result });
        }

        //更新訂單狀態並「產生」一筆通知紀錄存入資料庫
        private async Task<string> ProcessStatusChangeAsync(int CommissionId, string targetStatus)
        {
            var order = await _context.Commissions.FindAsync(CommissionId);
            if (order == null) return "訂單不存在";

            string title = "訂單狀態更新通知";
            string content = "";

            // 根據目標狀態決定通知內容
            switch (targetStatus)
            {
                case "審核中":
                    content = $"您的訂單 {order.Title} 正在審核中。";
                    break;
                case "審核失敗":
                    content = $"您的訂單 {order.Title} 審核失敗，請修改後重新提交。";
                    break;
                case "待接單":
                    content = $"您的訂單 {order.Title} 等待接取中。";
                    break;
                case "已接單":
                    content = $"您的訂單 {order.Title} 已經被接取。";
                    break;
                case "出貨中":
                    content = $"您的訂單 {order.Title} 正在準備出貨。";
                    break;
                case "已寄出":
                    content = $"您的訂單 {order.Title} 已寄出，請注意查收。";
                    break;
                case "已完成":
                    content = $"您的訂單 {order.Title} 已完成，感謝您的支持。";
                    break;
                default:
                    content = $"您的訂單 {order.Title} 狀態錯誤，請重新下單或聯繫客服人員。";
                    break;
            }

            // 1. 更新訂單狀態
            order.Status = targetStatus;
            _context.Notifications.Add(new Notification
            {
                Uid = order.CommissionId.ToString(),
                Title = title,
                Content = content,
            });

            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
            {
                title = title,
                content = content,
                time = DateTime.Now.ToString("HH:mm")
            });


            await _context.SaveChangesAsync();
            return $"訂單狀態已更新為{targetStatus}並存入通知";
        }

    }
}
