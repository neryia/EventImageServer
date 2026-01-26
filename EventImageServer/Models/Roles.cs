namespace EventImageServer.Models
{
    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public ICollection<Users> Users { get; set; }
    }
}
