#!/bin/sh
set -eu
for table in __EFMigrationsHistory Companies Users PlatformUsers Sales SaleItems ProductStocks Products CashSessions PurchaseReceipts CreditNotes; do
    exists=$(psql -X -At -v ON_ERROR_STOP=1 -c "SELECT to_regclass('public.\"$table\"') IS NOT NULL;" 2>/dev/null) \
        || { echo 'Restored schema connection failed.' >&2; exit 1; }
    [ "$exists" = t ] || { echo 'Restored schema verification failed.' >&2; exit 1; }
done
echo 'SCHEMA VERIFICATION PASS'
