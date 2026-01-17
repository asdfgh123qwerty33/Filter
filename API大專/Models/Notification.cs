namespace API大專.Models
{
    public class Notification
    {
        public int Id { get; set; }

        public string Uid { get; set; } = null!;

        public string? Title { get; set; }

        public string? Content { get; set; }

        public bool? IsRead { get; set; }

        public virtual User User { get; set; } = null!;
    }
}
