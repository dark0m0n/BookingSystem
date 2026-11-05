# Known gotchas

- **Observed:** Git does not track empty directories. Each boundary has a substantive README so the organization survives checkout.
- **Inferred-risk:** Folders and namespaces in one assembly do not prevent cross-boundary type access or state mutation. Follow the ownership guidance during review; introduce meaningful architecture checks as the domain grows; current constructor tests do not enforce boundary isolation.
- **Observed:** The seven folder names do not mean booking, authentication, payment, or notification functionality exists. Their guides and the model bundle distinguish declared entities from future workflows.
- **Unverified:** Atomic cross-boundary consistency, provider integration, refunds, and delivery failure handling remain unimplemented. Local hold expiry, cancellation deadlines, and payment outcomes are now covered by the lifecycle bundle; fixed timing policies remain unspecified. Resolve them as part of their individual features.

The boundary organization itself has no runtime behavior. Subsequent entity constructor tests are described in the model bundle; those tests do not demonstrate boundary isolation or booking workflows. No service extraction or database migration is part of this change.
