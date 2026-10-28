import type { ReactNode } from 'react';

export function SectionTitle({ number, title, note }: { number: string; title: string; note: string }) {
    return <div className="section-heading"><span className="step">{number}</span><div><h2>{title}</h2><p>{note}</p></div></div>;
}

export function Notice({ children, kind }: { children: ReactNode; kind?: 'error' }) {
    return <div className={kind === 'error' ? 'notice error-notice' : 'setup-notice'} role={kind === 'error' ? 'alert' : 'status'}>{children}</div>;
}
