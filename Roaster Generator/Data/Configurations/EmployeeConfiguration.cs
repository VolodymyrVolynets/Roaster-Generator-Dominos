using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Roaster_Generator.Entities;

namespace Roaster_Generator.Data.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");

        builder.HasKey(employee => employee.Id);

        builder.Property(employee => employee.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(employee => employee.EmployeeNumber)
            .HasColumnName("employee_number")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(employee => employee.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(employee => employee.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(employee => employee.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(employee => employee.PayrollNumber)
            .HasColumnName("payroll_number")
            .HasMaxLength(64);

        builder.Property(employee => employee.HourlyRate)
            .HasColumnName("hourly_rate")
            .HasPrecision(12, 2)
            .HasDefaultValue(14.5m)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(employee => employee.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasIndex(employee => employee.EmployeeNumber)
            .IsUnique();

        builder.HasIndex(employee => employee.PayrollNumber)
            .IsUnique()
            .HasFilter("payroll_number IS NOT NULL");

        builder.HasData(
            new Employee
            {
                Id = Guid.Parse("3a27962b-3138-4e8c-9c5c-2fa7c817073f"),
                EmployeeNumber = "9100",
                FirstName = "Marcin",
                LastName = "Galkowski",
                PhoneNumber = "0858340019"
            },
            new Employee
            {
                Id = Guid.Parse("00495efa-eb46-43f6-bbe9-fa56b28bb9a2"),
                EmployeeNumber = "7870",
                FirstName = "Arshad B",
                LastName = "omarkhel",
                PhoneNumber = "0899427870"
            },
            new Employee
            {
                Id = Guid.Parse("5ad2fc14-cda5-482e-a48a-14bbd43333ed"),
                EmployeeNumber = "6116",
                FirstName = "Tony",
                LastName = "Lukose",
                PhoneNumber = "0894466116"
            },
            new Employee
            {
                Id = Guid.Parse("fe3538ee-640b-44dc-895e-e859eec2d2c7"),
                EmployeeNumber = "7229",
                FirstName = "Pavishkumar B",
                LastName = "Kumararamalingam",
                PhoneNumber = "0892597229"
            },
            new Employee
            {
                Id = Guid.Parse("3b64e926-651a-422b-bc62-da27dd846356"),
                EmployeeNumber = "2075",
                FirstName = "Shahram B",
                LastName = "Sajawal",
                PhoneNumber = "0858402075"
            },
            new Employee
            {
                Id = Guid.Parse("e2aeb96b-6937-42ac-8e67-37fe06876a9e"),
                EmployeeNumber = "5385",
                FirstName = "Mustafa B",
                LastName = "Kanchwala",
                PhoneNumber = "0894915385"
            },
            new Employee
            {
                Id = Guid.Parse("4777bddb-029a-4248-8393-6bea8a61f7ae"),
                EmployeeNumber = "2004",
                FirstName = "Subhan",
                LastName = "Aqeel",
                PhoneNumber = "0831243953"
            },
            new Employee
            {
                Id = Guid.Parse("6077cbdb-db89-4643-96ab-a1c2e32cfee1"),
                EmployeeNumber = "2138",
                FirstName = "Nanthu",
                LastName = "Njaneswaran",
                PhoneNumber = "0872462138"
            },
            new Employee
            {
                Id = Guid.Parse("4da853a0-b154-4fce-84d3-63512d0ba73f"),
                EmployeeNumber = "3889",
                FirstName = "Pradeep",
                LastName = "Sreekumari",
                PhoneNumber = "0894133889"
            },
            new Employee
            {
                Id = Guid.Parse("d81a3e8a-0a9c-47fd-b568-5cbc2eef9f17"),
                EmployeeNumber = "1840",
                FirstName = "ANDREWS",
                LastName = "ABRAHAM CHACKO",
                PhoneNumber = "0831431840"
            },
            new Employee
            {
                Id = Guid.Parse("9e3526b7-d00c-4bd3-b05f-7d15ece9d21b"),
                EmployeeNumber = "3512",
                FirstName = "Bibin",
                LastName = "Baby",
                PhoneNumber = "0892403512"
            },
            new Employee
            {
                Id = Guid.Parse("f7143b02-3df5-4f28-95a5-6946ddfade4f"),
                EmployeeNumber = "7179",
                FirstName = "SunilValliparampil",
                LastName = "Alex",
                PhoneNumber = "0892097179"
            },
            new Employee
            {
                Id = Guid.Parse("b40abf4f-0a67-4a68-b39d-98ce2431efae"),
                EmployeeNumber = "3040",
                FirstName = "Jomon",
                LastName = "Thottiparambil Johny",
                PhoneNumber = "0874520403"
            },
            new Employee
            {
                Id = Guid.Parse("796f9cd8-7132-45e4-b4ce-afdce4476e22"),
                EmployeeNumber = "5512",
                FirstName = "Rajendran",
                LastName = "Pandiarajan",
                PhoneNumber = "0892315512"
            },
            new Employee
            {
                Id = Guid.Parse("cfddadab-f4b8-4fbd-945c-4faef19ef2e4"),
                EmployeeNumber = "7090",
                FirstName = "WEI",
                LastName = "WANG",
                PhoneNumber = "0870570907"
            },
            new Employee
            {
                Id = Guid.Parse("f07d963f-272a-4636-a7e5-ee6af713bfa5"),
                EmployeeNumber = "8944",
                FirstName = "Alban",
                LastName = "Keane",
                PhoneNumber = "0858388944"
            },
            new Employee
            {
                Id = Guid.Parse("877293ba-4650-488a-a0a7-1eff8f5d02f5"),
                EmployeeNumber = "9627",
                FirstName = "Joveski",
                LastName = "Jovche",
                PhoneNumber = "0894262021"
            },
            new Employee
            {
                Id = Guid.Parse("b37367e7-6fed-454b-95c5-646e5d755130"),
                EmployeeNumber = "0741",
                FirstName = "Ronan",
                LastName = "O'Dwyer",
                PhoneNumber = "0852860741"
            },
            new Employee
            {
                Id = Guid.Parse("99c0cddf-f6e8-4bba-a86f-33952523d0bb"),
                EmployeeNumber = "5062",
                FirstName = "Shajahan",
                LastName = "Shajahan",
                PhoneNumber = "0894045062"
            },
            new Employee
            {
                Id = Guid.Parse("03a5c5c9-8620-49af-b7da-e89c6a7ce123"),
                EmployeeNumber = "2458",
                FirstName = "Chowdhury",
                LastName = "Belal",
                PhoneNumber = "0894582680"
            }
        );

        builder.HasOne(employee => employee.User)
            .WithOne(user => user.Employee)
            .HasForeignKey<ApplicationUser>(user => user.EmployeeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
