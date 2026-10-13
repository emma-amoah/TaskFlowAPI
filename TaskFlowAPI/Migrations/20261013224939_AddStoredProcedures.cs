using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlowAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE PROCEDURE sp_GetUserProductivityReport
                    @UserId NVARCHAR(450)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT 
                        COUNT(CASE WHEN Status = 'Completed' THEN 1 END) AS CompletedTasksCount,
                        COUNT(CASE WHEN Status != 'Completed' THEN 1 END) AS ActiveTasksCount,
                        COUNT(CASE WHEN DueDate < GETUTCDATE() AND Status != 'Completed' THEN 1 END) AS OverdueTasksCount
                    FROM Tasks 
                    WHERE AssignedToId = @UserId AND IsDeleted = 0;
                END");

            // Stored Procedure 2: Complete Project Burn-down Calculation Status
            migrationBuilder.Sql(@"
                CREATE PROCEDURE sp_GetProjectBurndownStatus
                    @ProjectId INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT 
                        Status,
                        COUNT(*) AS TaskCount
                    FROM Tasks
                    WHERE ProjectId = @ProjectId AND IsDeleted = 0
                    GROUP BY Status;
                END");

            // Stored Procedure 3: Quick System Purge for Expired Refresh Tokens
            migrationBuilder.Sql(@"
                CREATE PROCEDURE sp_PurgeExpiredRefreshTokens
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DELETE FROM RefreshTokens 
                    WHERE ExpiresAt < GETUTCDATE() OR RevokedAt IS NOT NULL;
                END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE sp_GetUserProductivityReport");
            migrationBuilder.Sql("DROP PROCEDURE sp_GetProjectBurndownStatus");
            migrationBuilder.Sql("DROP PROCEDURE sp_PurgeExpiredRefreshTokens");
        }
    }
}
