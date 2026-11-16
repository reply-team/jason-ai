using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DecisionThread : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "thread_id",
                table: "decisions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thread_plugin_id",
                table: "decisions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            // A question raised before this migration names its thread by the rule a raise follows from now on, so that
            // null means "about no thread the runtime vouches for" of every row, the older ones too: a question of the
            // runtime's own responder (role 'responder', created by the system) takes the plugin and the thread of the
            // reserved key inbound_reply, unless a line of the item says anybody but the system patched that key, since
            // workitem.update may rewrite any key of an open item's context; the dispatcher's own patch of a further move
            // is the system's. It reads each item's context and lines as they stand at this migration, not as they stood at
            // the raise: the respond step patches a further move onto the key and never its thread. Every row the rule
            // matches, pending, answered or cancelled. Guarded, so that no stored context can fail the migration and with it
            // the start: a context that is not valid JSON, or whose two values are not both text, is not read, and its row
            // stays null. CASE, not AND, because only CASE promises that json_valid is asked before json_type reads the text.
            migrationBuilder.Sql(
                """
                UPDATE decisions
                SET thread_plugin_id = (SELECT json_extract(w.context_json, '$.inbound_reply.plugin') FROM work_items w WHERE w.id = decisions.work_item_id),
                    thread_id = (SELECT json_extract(w.context_json, '$.inbound_reply.thread') FROM work_items w WHERE w.id = decisions.work_item_id)
                WHERE EXISTS (
                    SELECT 1 FROM work_items w
                    WHERE w.id = decisions.work_item_id
                      AND w.role = 'responder'
                      AND w.created_by_type = 'system'
                      AND CASE WHEN json_valid(w.context_json)
                               THEN json_type(w.context_json, '$.inbound_reply.plugin') = 'text'
                                    AND json_type(w.context_json, '$.inbound_reply.thread') = 'text'
                               ELSE 0 END
                      AND NOT EXISTS (
                          SELECT 1 FROM journal p
                          WHERE p.work_item_id = w.public_id
                            AND p.kind = 'workitem_context_updated'
                            AND p.actor_type <> 'system'
                            AND p.key = 'inbound_reply'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropped in place rather than by rebuilding the table, which would lose what an earlier migration wrote on it
            // and, with foreign keys on, the rows deleted with a row of it.
            migrationBuilder.Sql("ALTER TABLE decisions DROP COLUMN thread_id;");
            migrationBuilder.Sql("ALTER TABLE decisions DROP COLUMN thread_plugin_id;");
        }
    }
}
