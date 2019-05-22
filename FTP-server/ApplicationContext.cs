using Microsoft.EntityFrameworkCore;


namespace FTP_server
{
    public class ApplicationContext : DbContext
    {
        public DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=base.db");
        }
    }
}
