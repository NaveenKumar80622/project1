using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmBusReservationStoredProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP PROCEDURE IF EXISTS sp_ConfirmBusReservation;
            ");
            migrationBuilder.Sql(@"
                CREATE PROCEDURE sp_ConfirmBusReservation(
                    IN p_BusBookingId INT,
                    IN p_SeatCode VARCHAR(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci,
                    OUT p_Success TINYINT
                )
                BEGIN
                    DECLARE v_IsBooked BOOLEAN;
                    
                    SELECT IsBooked INTO v_IsBooked 
                    FROM bus_seats 
                    WHERE BusBookingId = p_BusBookingId AND SeatCode = p_SeatCode 
                    FOR UPDATE;
                    
                    IF v_IsBooked = FALSE THEN
                        UPDATE bus_seats 
                        SET IsBooked = TRUE 
                        WHERE BusBookingId = p_BusBookingId AND SeatCode = p_SeatCode;
                        SET p_Success = 1;
                    ELSE
                        SET p_Success = 0;
                    END IF;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_ConfirmBusReservation;");
        }
    }
}
