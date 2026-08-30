using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncHotelCacheSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EMPTY MIGRATION: 
            // The database is shared with GuruPickNBook.Api, which has already created 
            // the `hotel_info_caches` table and modified `hotel_reservations`.
            // We empty this so PickNBook.Api does not attempt to execute duplicate DDL.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // EMPTY MIGRATION
        }
    }
}
