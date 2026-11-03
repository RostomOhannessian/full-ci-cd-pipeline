using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Mapping;

/// <summary>Maps <see cref="OutboxMessage"/> to the table <c>catalog.OutboxMessages</c> (plan section 8.4).</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>The most characters of a stored failure description.</summary>
    public const int LastErrorLength = 500;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OutboxMessages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.EventType).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(message => message.SchemaVersion).IsRequired();
        builder.Property(message => message.OccurredAt).IsRequired();
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(StatusColumn.Length).IsUnicode(false).IsRequired();
        builder.Property(message => message.Attempts).IsRequired();
        builder.Property(message => message.CreatedAt).IsRequired();
        builder.Property(message => message.NextAttemptAt).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(LastErrorLength).IsRequired(false);

        // The processor reads the pending messages that are due, oldest first, and this filtered index holds only those, so the lease query touches no finished row.
        builder.HasIndex(message => new { message.NextAttemptAt, message.Id }).HasDatabaseName("IX_OutboxMessages_Pending").HasFilter("[Status] = 'Pending'");

        // The cleanup job deletes finished messages after their retention, and this index finds them.
        builder.HasIndex(message => message.CompletedAt).HasDatabaseName("IX_OutboxMessages_CompletedAt").HasFilter("[Status] = 'Done'");
    }
}

/// <summary>Maps <see cref="OutboxReceipt"/> to the table <c>catalog.OutboxReceipts</c> (plan section 8.4).</summary>
public sealed class OutboxReceiptConfiguration : IEntityTypeConfiguration<OutboxReceipt>
{
    /// <summary>The most characters of the name of a handler.</summary>
    public const int HandlerLength = 200;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxReceipt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OutboxReceipts");

        builder.HasKey(receipt => new { receipt.MessageId, receipt.Handler });
        builder.Property(receipt => receipt.Handler).HasMaxLength(HandlerLength).IsUnicode(false).IsRequired();
        builder.Property(receipt => receipt.ProcessedAt).IsRequired();

        // A receipt goes with its message, so deleting a finished message in the cleanup takes its receipts along.
        builder.HasOne<OutboxMessage>().WithMany().HasForeignKey(receipt => receipt.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="IdempotencyRecord"/> to the table <c>catalog.IdempotencyRecords</c> (plan section 8.4).</summary>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    /// <summary>The most characters of a caller identifier, which is the <c>sub</c> or the <c>azp</c> claim of a token.</summary>
    public const int CallerIdLength = 256;

    /// <summary>The length of a fingerprint, which is the hexadecimal text of a SHA-256 hash.</summary>
    public const int FingerprintLength = 64;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("IdempotencyRecords");

        // A key belongs to one caller, so the pair is the identity. The key is printable ASCII of at most 128 characters (ADR-0028).
        builder.HasKey(record => new { record.CallerId, record.Key });
        builder.Property(record => record.CallerId).HasMaxLength(CallerIdLength).IsRequired();
        builder.Property(record => record.Key).HasMaxLength(Application.Idempotency.IdempotencyKey.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(record => record.Fingerprint).HasMaxLength(FingerprintLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(record => record.Status).HasConversion<string>().HasMaxLength(StatusColumn.Length).IsUnicode(false).IsRequired();
        builder.Property(record => record.Response).IsRequired(false);
        builder.Property(record => record.CreatedAt).IsRequired();
        builder.Property(record => record.ExpiresAt).IsRequired();

        // The cleanup job deletes the records that expired, and this index finds them.
        builder.HasIndex(record => record.ExpiresAt).HasDatabaseName("IX_IdempotencyRecords_ExpiresAt");
    }
}
