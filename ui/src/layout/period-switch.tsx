import type { Period } from '../router';
import { periodLabels } from '../time/periods';
import { Segmented } from './segmented';

const campaignsPeriods: readonly Period[] = ['today', '7d', '30d'];

/** Today, the last seven days, thirty or the periods a screen offers, each ending now in the browser's time zone. */
export function PeriodSwitch({
    value,
    onChange,
    periods = campaignsPeriods,
}: {
    value: Period;
    onChange: (period: Period) => void;
    periods?: readonly Period[];
}) {
    return (
        <Segmented
            label="Period"
            options={periods.map((period) => ({ value: period, label: periodLabels[period] }))}
            value={value}
            onChange={onChange}
        />
    );
}
